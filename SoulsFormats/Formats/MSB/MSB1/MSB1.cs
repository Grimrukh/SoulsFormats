using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoulsFormats
{
    /// <summary>
    /// A map layout file used in DS1. Extension: .msb
    /// </summary>
    public partial class MSB1 : SoulsFile<MSB1>, IMsb
    {
        /// <summary>
        /// True for PS3/X360, false for PC.
        /// </summary>
        public bool BigEndian { get; set; }

        /// <summary>
        /// Model files that are available for parts to use.
        /// </summary>
        public ModelParam Models { get; set; }
        IMsbParam<IMsbModel> IMsb.Models => Models;

        /// <summary>
        /// Dynamic or interactive systems such as item pickups, levers, enemy spawners, etc.
        /// </summary>
        public EventParam Events { get; set; }
        IMsbParam<IMsbEvent> IMsb.Events => Events;

        /// <summary>
        /// Points or areas of space that trigger some sort of behavior.
        /// </summary>
        public PointParam Regions { get; set; }
        IMsbParam<IMsbRegion> IMsb.Regions => Regions;

        /// <summary>
        /// Instances of actual things in the map.
        /// </summary>
        public PartsParam Parts { get; set; }
        IMsbParam<IMsbPart> IMsb.Parts => Parts;

        internal class Entries
        {
            public EntryCollection<Model> Models { get; private set; }
            public EntryCollection<Event> Events { get; private set; }
            public EntryCollection<Event.Environment> Environments { get; private set; }
            public EntryCollection<Region> Regions { get; private set; }
            public EntryCollection<Part> Parts { get; private set; }
            public EntryCollection<Part.Collision> Collisions { get; private set; }

            public Entries(
                List<Model> models, List<Event> events, List<Event.Environment> environments, 
                List<Region> regions, List<Part> parts, List<Part.Collision> collisions)
            {
                Models = new EntryCollection<Model>(models);
                Events = new EntryCollection<Event>(events);
                Environments = new EntryCollection<Event.Environment>(environments);
                Regions = new EntryCollection<Region>(regions);
                Parts = new EntryCollection<Part>(parts);
                Collisions = new EntryCollection<Part.Collision>(collisions);
            }

            public Entries(MSB1 msb)
            {
                Models = new EntryCollection<Model>(msb.Models.GetEntries());
                Events = new EntryCollection<Event>(msb.Events.GetEntries());
                Environments = new EntryCollection<Event.Environment>(msb.Events.Environments.ToList());
                Regions = new EntryCollection<Region>(msb.Regions.GetEntries());
                Parts = new EntryCollection<Part>(msb.Parts.GetEntries());
                Collisions = new EntryCollection<Part.Collision>(msb.Parts.Collisions.ToList());
            }
            
            public Dictionary<string, int> CountModelInstances()
            {
                Dictionary<string, int> modelCounts = new();
                foreach (Part part in Parts)
                {
                    if (!string.IsNullOrEmpty(part.ModelName) && !modelCounts.TryAdd(part.ModelName, 1))
                        modelCounts[part.ModelName]++;
                }

                return modelCounts;
            }
        }

        /// <summary>
        /// Creates an empty MSB1.
        /// </summary>
        public MSB1()
        {
            Models = new ModelParam();
            Events = new EventParam();
            Regions = new PointParam();
            Parts = new PartsParam();
        }

        /// <summary>
        /// Deserializes file data from a stream.
        /// </summary>
        protected override void Read(BinaryReaderEx br)
        {
            br.BigEndian = false;
            br.BigEndian = BigEndian = br.GetUInt32(4) > 0xFFFF;

            Models = new ModelParam();
            List<Model> models = Models.Read(br);
            Events = new EventParam();
            List<Event> events = Events.Read(br);
            Regions = new PointParam();
            List<Region> regions = Regions.Read(br);
            Parts = new PartsParam();
            List<Part> parts = Parts.Read(br);

            if (br.Position != 0)
                throw new InvalidDataException("The next param offset of the final param should be 0, but it wasn't.");

            MSB.DisambiguateNames(models);
            MSB.DisambiguateNames(regions);
            MSB.DisambiguateNames(parts);
            
            Entries entries = new(models, events, Events.Environments, regions, parts, Parts.Collisions);

            foreach (Event evt in entries.Events)
                evt.GetNames(entries);
            foreach (Part part in entries.Parts)
                part.GetNames(entries);
        }

        /// <summary>
        /// Serializes file data to a stream.
        /// </summary>
        protected override void Write(BinaryWriterEx bw)
        {
            Entries entries = new(this);

            // Make a dictionary mapping each model name to its number of uses.

            Dictionary<string, int> modelCounts = entries.CountModelInstances();
            foreach (Model model in entries.Models)
                model.CountInstances(modelCounts);
            foreach (Event evt in entries.Events)
                evt.GetIndices(this, entries);
            foreach (Part part in entries.Parts)
                part.GetIndices(entries);

            bw.BigEndian = BigEndian;

            Models.Write(bw, entries.Models.Items);
            bw.FillInt32("NextParamOffset", (int)bw.Position);
            Events.Write(bw, entries.Events.Items);
            bw.FillInt32("NextParamOffset", (int)bw.Position);
            Regions.Write(bw, entries.Regions.Items);
            bw.FillInt32("NextParamOffset", (int)bw.Position);
            Parts.Write(bw, entries.Parts.Items);
            bw.FillInt32("NextParamOffset", 0);
        }

        /// <summary>
        /// A generic group of entries in an MSB.
        /// </summary>
        public abstract class Param<T> where T : Entry
        {
            /// <summary>
            /// A string identifying the type of entries in the param.
            /// </summary>
            internal abstract string Name { get; }

            internal List<T> Read(BinaryReaderEx br)
            {
                br.AssertInt32(0);
                int nameOffset = br.ReadInt32();
                int offsetCount = br.ReadInt32();
                int[] entryOffsets = br.ReadInt32s(offsetCount - 1);
                int nextParamOffset = br.ReadInt32();

                string name = br.GetASCII(nameOffset);
                if (name != Name)
                    throw new InvalidDataException($"Expected param \"{Name}\", got param \"{name}\"");

                var entries = new List<T>(offsetCount - 1);
                foreach (int offset in entryOffsets)
                {
                    br.Position = offset;
                    entries.Add(ReadEntry(br));
                }
                br.Position = nextParamOffset;
                return entries;
            }

            internal abstract T ReadEntry(BinaryReaderEx br);

            internal virtual void Write(BinaryWriterEx bw, List<T> entries)
            {
                bw.WriteInt32(0);
                bw.ReserveInt32("ParamNameOffset");
                bw.WriteInt32(entries.Count + 1);
                for (int i = 0; i < entries.Count; i++)
                    bw.ReserveInt32($"EntryOffset{i}");
                bw.ReserveInt32("NextParamOffset");

                bw.FillInt32("ParamNameOffset", (int)bw.Position);
                bw.WriteASCII(Name, true);
                bw.Pad(4);

                int id = 0;
                Type type = null;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (type != entries[i].GetType())
                    {
                        type = entries[i].GetType();
                        id = 0;
                    }

                    bw.FillInt32($"EntryOffset{i}", (int)bw.Position);
                    entries[i].Write(bw, id);
                    id++;
                }
            }

            /// <summary>
            /// Returns all of the entries in this param, in the order they will be written to the file.
            /// </summary>
            public abstract List<T> GetEntries();

            /// <summary>
            /// Returns the name of the param as a string.
            /// </summary>
            public override string ToString()
            {
                return $"{Name}";
            }
        }

        /// <summary>
        /// A generic entry in an MSB param.
        /// </summary>
        public abstract class Entry : IMsbEntry
        {
            /// <summary>
            /// The name of this entry.
            /// </summary>
            public string Name { get; set; }

            internal abstract void Write(BinaryWriterEx bw, int id);
        }
    }
}
