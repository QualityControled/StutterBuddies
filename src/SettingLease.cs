using System;
using System.Collections.Generic;

namespace StutterBuddies
{
    // Only restore values still owned by this instance. An external write wins.
    internal sealed class SettingLease<T>
    {
        private readonly Func<T> read;
        private readonly Action<T> write;
        private readonly EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        private T original;
        private T applied;
        public bool Active { get; private set; }

        public SettingLease(Func<T> read, Action<T> write)
        {
            this.read = read;
            this.write = write;
        }

        public bool StillOwned => !Active || comparer.Equals(read(), applied);

        public void Apply(T value)
        {
            if (Active) throw new InvalidOperationException("Release the setting before reapplying it.");
            original = read();
            applied = value;
            if (comparer.Equals(original, value)) return;
            write(value);
            Active = true;
        }

        public void Release()
        {
            if (!Active) return;
            if (StillOwned) write(original);
            Active = false;
        }
    }
}
