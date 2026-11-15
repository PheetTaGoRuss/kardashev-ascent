using System;
using System.Collections.Generic;
using UnityEngine;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// One window per game system (overview, power, robots, manufacturing, research, network...). Each window registers
    /// here; the tab bar lists them, and opening one closes the others so they can share the same place on screen.
    /// A future system adds one Register call (and its window) and gets a tab.
    /// </summary>
    public static class SystemTabs
    {
        public sealed class Tab
        {
            public string Id, Title;
            public KeyCode Key;
            public int Order;
            public Func<bool> IsOpen;
            public Action<bool> SetOpen;
            /// <summary>Null when available; otherwise why it is locked (shown in its window).</summary>
            public Func<string> LockedReason;
        }

        static readonly List<Tab> tabs = new List<Tab>();

        public static IReadOnlyList<Tab> All => tabs;

        public static event Action Changed;

        public static void Register(Tab tab)
        {
            tabs.RemoveAll(t => t.Id == tab.Id);
            tabs.Add(tab);
            tabs.Sort((a, b) => a.Order.CompareTo(b.Order));
            Changed?.Invoke();
        }

        public static void Unregister(string id)
        {
            if (tabs.RemoveAll(t => t.Id == id) > 0)
                Changed?.Invoke();
        }

        /// <summary>Open this system's window (closing the others), or close it if it is already open.</summary>
        public static void Toggle(string id)
        {
            Tab tab = tabs.Find(t => t.Id == id);
            if (tab == null)
                return;
            bool open = !tab.IsOpen();
            if (open)
                foreach (Tab other in tabs)
                    if (other != tab && other.IsOpen())
                        other.SetOpen(false);
            tab.SetOpen(open);
        }

        /// <summary>Open this system's window if it isn't open (closing the others); never closes it.</summary>
        public static void Open(string id)
        {
            Tab tab = tabs.Find(t => t.Id == id);
            if (tab != null && !tab.IsOpen())
                Toggle(id);
        }

        /// <summary>A window opened itself (e.g. by its key): close the others.</summary>
        public static void Opened(string id)
        {
            foreach (Tab other in tabs)
                if (other.Id != id && other.IsOpen())
                    other.SetOpen(false);
        }

        public static bool AnyOpen
        {
            get
            {
                foreach (Tab t in tabs)
                    if (t.IsOpen()) return true;
                return false;
            }
        }
    }
}
