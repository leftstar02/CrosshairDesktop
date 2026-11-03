using System.Collections.Generic;

namespace CrosshairDesktop
{
    internal sealed class ShortcutRecorder
    {
        private readonly HashSet<int> down = new HashSet<int>(), chord = new HashSet<int>();
        internal KeyBinding Candidate { get; private set; }
        internal bool Ready { get { return Candidate != null && down.Count == 0; } }
        internal void Reset() { down.Clear(); chord.Clear(); Candidate = null; }
        internal bool Feed(int key, bool pressed)
        {
            if (!KeyBinding.IsKeyboardKey(key)) return false;
            if (pressed)
            {
                if (Ready) Reset();
                if (!down.Add(key)) return false;
                chord.Add(key);
            }
            else if (!down.Remove(key)) return false;
            Candidate = KeyBinding.FromKeys(chord, "Release"); return true;
        }
    }
}
