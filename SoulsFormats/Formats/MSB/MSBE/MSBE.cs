using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Linq;

namespace SoulsFormats
{
    /// <summary>
    /// A map layout file used in Sekiro. Extension: .msb
    /// </summary>
    public partial class MSBE : SoulsFile<MSBE>, IMsb
    {
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
        /// Unknown, but related to muffling regions somehow.
        /// </summary>
        public RouteParam Routes { get; set; }

        /// <summary>
        /// Instances of actual things in the map.
        /// </summary>
        public PartsParam Parts { get; set; }
        IMsbParam<IMsbPart> IMsb.Parts => Parts;

        /// <summary>
        /// Unknown and unused.
        /// </summary>
        public EmptyParam Layers { get; set; }

        /// <summary>
        /// Creates an MSBS with nothing in it.
        /// </summary>
        public MSBE()
        {
            Models = new ModelParam();
            Events = new EventParam();
            Regions = new PointParam();
            Routes = new RouteParam();
            Parts = new PartsParam();
            Layers = new EmptyParam(0x23, "LAYER_PARAM_ST");
        }

        /// <summary>
        /// Checks whether the data appears to be a file of this format.
        /// </summary>
        protected override bool Is(BinaryReaderEx br)
        {
            if (br.Length < 4)
                return false;

            string magic = br.GetASCII(0, 4);
            return magic == "MSB ";
        }

        /// <summary>
        /// Deserializes file data from a stream.
        /// </summary>
        protected override void Read(BinaryReaderEx br)
        {
            br.BigEndian = false;
            MSB.AssertHeader(br);

            Models = new ModelParam();
            List<Model> modelEntries = Models.Read(br);
            Events = new EventParam();
            List<Event> eventEntries = Events.Read(br);
            Regions = new PointParam();
            List<Region> regionEntries = Regions.Read(br);
            Routes = new RouteParam();
            List<Route> routeEntries = Routes.Read(br);
            
            Layers = new EmptyParam(0x49, "LAYER_PARAM_ST");
            Layers.Read(br);
            
            Parts = new PartsParam();
            List<Part> partEntries = Parts.Read(br);

            if (br.Position != 0)
                throw new InvalidDataException("The next param offset of the final param should be 0, but it wasn't.");

            MSB.DisambiguateNames(modelEntries);
            MSB.DisambiguateNames(regionEntries);
            MSB.DisambiguateNames(partEntries);
            MSB.DisambiguateNames(eventEntries);
            
            Entries entries = new(
                modelEntries, eventEntries, Events.PatrolInfo,
                regionEntries, routeEntries, partEntries, Parts.Collisions);

            foreach (Event evt in entries.Events)
                evt.GetNames(entries);
            foreach (Region region in entries.Regions)
                region.GetNames(entries);
            foreach (Part part in entries.Parts)
                part.GetNames(entries);
        }

        /// <summary>
        /// Serializes file data to a stream.
        /// </summary>
        protected override void Write(BinaryWriterEx bw)
        {
            Entries entries = new(this);

            Dictionary<string, int> modelCounts = entries.CountModelInstances();
            foreach (Model model in entries.Models)
                model.CountInstances(modelCounts);
            foreach (Event evt in entries.Events)
                evt.GetIndices(entries);
            foreach (Region region in entries.Regions)
                region.GetIndices(entries);
            foreach (Part part in entries.Parts)
                part.GetIndices(entries);

            bw.BigEndian = false;
            MSB.WriteHeader(bw);

            Models.Write(bw, entries.Models.Items);
            bw.FillInt64("NextParamOffset", bw.Position);
            Events.Write(bw, entries.Events.Items);
            bw.FillInt64("NextParamOffset", bw.Position);
            Regions.Write(bw, entries.Regions.Items);
            bw.FillInt64("NextParamOffset", bw.Position);
            Routes.Write(bw, entries.Routes.Items);
            bw.FillInt64("NextParamOffset", bw.Position);
            Layers.Write(bw, Layers.GetEntries());
            bw.FillInt64("NextParamOffset", bw.Position);
            Parts.Write(bw, entries.Parts.Items);
            bw.FillInt64("NextParamOffset", 0);
        }

        internal class Entries
        {
            public EntryCollection<Model> Models { get; private set; }
            public EntryCollection<Event> Events { get; private set; }
            public EntryCollection<Event.PatrolInfo> PatrolInfos { get; private set; }
            public EntryCollection<Region> Regions { get; private set; }
            public EntryCollection<Route> Routes { get; private set; }
            public EntryCollection<Part> Parts { get; private set; }
            public EntryCollection<Part.Collision> Collisions { get; private set; }

            public Entries(
                List<Model> models, List<Event> events, List<Event.PatrolInfo> patrolInfos, 
                List<Region> regions, List<Route> routes, List<Part> parts, List<Part.Collision> collisions)
            {
                Models = new EntryCollection<Model>(models);
                Events = new EntryCollection<Event>(events);
                PatrolInfos = new EntryCollection<Event.PatrolInfo>(patrolInfos);
                Regions = new EntryCollection<Region>(regions);
                Routes = new EntryCollection<Route>(routes);
                Parts = new EntryCollection<Part>(parts);
                Collisions = new EntryCollection<Part.Collision>(collisions);
            }

            public Entries(MSBE msb)
            {
                Models = new EntryCollection<Model>(msb.Models.GetEntries());
                Events = new EntryCollection<Event>(msb.Events.GetEntries());
                PatrolInfos = new EntryCollection<Event.PatrolInfo>(msb.Events.PatrolInfo.ToList());
                Regions = new EntryCollection<Region>(msb.Regions.GetEntries());
                Routes = new EntryCollection<Route>(msb.Routes.GetEntries());
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
        /// A generic group of entries in an MSB.
        /// </summary>
        public abstract class Param<T> where T : Entry
        {
            /// <summary>
            /// Unknown; probably some kind of version number.
            /// </summary>
            public int Version { get; set; }

            private protected string Name { get; }

            internal Param(int version, string name)
            {
                Version = version;
                Name = name;
            }

            internal List<T> Read(BinaryReaderEx br)
            {
                Version = br.ReadInt32();
                int offsetCount = br.ReadInt32();
                long nameOffset = br.ReadInt64();
                long[] entryOffsets = br.ReadInt64s(offsetCount - 1);
                long nextParamOffset = br.ReadInt64();

                string name = br.GetUTF16(nameOffset);
                if (name != Name)
                    throw new InvalidDataException($"Expected param \"{Name}\", got param \"{name}\"");

                var entries = new List<T>(offsetCount - 1);
                foreach (long offset in entryOffsets)
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
                bw.WriteInt32(Version);
                bw.WriteInt32(entries.Count + 1);
                bw.ReserveInt64("ParamNameOffset");
                for (int i = 0; i < entries.Count; i++)
                    bw.ReserveInt64($"EntryOffset{i}");
                bw.ReserveInt64("NextParamOffset");

                bw.FillInt64("ParamNameOffset", bw.Position);
                bw.WriteUTF16(Name, true);
                bw.Pad(8);

                int id = 0;
                Type type = null;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (type != entries[i].GetType())
                    {
                        type = entries[i].GetType();
                        id = 0;
                    }

                    bw.FillInt64($"EntryOffset{i}", bw.Position);
                    entries[i].Write(bw, id);
                    id++;
                }
            }

            /// <summary>
            /// Returns all of the entries in this param, in the order they will be written to the file.
            /// </summary>
            public abstract List<T> GetEntries();

            /// <summary>
            /// Returns the version number and name of the param as a string.
            /// </summary>
            public override string ToString()
            {
                return $"0x{Version:X2} {Name}";
            }
        }

        /// <summary>
        /// A generic entry in an MSB param.
        /// </summary>
        [DataContract]
        public abstract class Entry : IMsbEntry
        {
            /// <summary>
            /// The name of this entry.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            internal abstract void Write(BinaryWriterEx bw, int id);
        }

        /// <summary>
        /// Used to represent unused params that should never have any entries in them.
        /// </summary>
        public class EmptyParam : Param<Entry>
        {
            /// <summary>
            /// Creates an EmptyParam with the given values.
            /// </summary>
            public EmptyParam(int version, string name) : base(version, name) { }

            internal override Entry ReadEntry(BinaryReaderEx br)
            {
                throw new InvalidDataException($"Expected param \"{Name}\" to be empty, but it wasn't.");
            }

            /// <summary>
            /// Returns an empty list.
            /// </summary>
            public override List<Entry> GetEntries()
            {
                return new List<Entry>();
            }
        }
    }
}
