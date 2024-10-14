using System;
using System.Collections.Generic;

namespace SoulsFormats;


internal class EntryCollection<T> where T : IMsbEntry
{
    public List<T> Items { get; }
    public List<string> Names { get; }
    public Dictionary<string, int> Indices { get; }

    public EntryCollection(List<T> items)
    {
        Items = items;
        Names = new List<string>();
        Indices = new Dictionary<string, int>();

        for (int i = 0; i < items.Count; i++)
        {
            string name = items[i].Name;
            Names.Add(name);
            Indices[name] = i;
        }
    }
    
    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
}
