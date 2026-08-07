using System;
using System.Collections.Generic;
using System.Linq;

namespace Glitch.Infrastructure
{
    /// <summary>
    /// Serializes native mutations with account fencing. A fence cannot begin in
    /// the middle of a native mutator, and a fenced account cannot start another
    /// mutation unless the caller explicitly identifies it as part of Flatten.
    /// </summary>
    internal sealed class GlitchMutationGate
    {
        private readonly object _gate = new object();
        private readonly HashSet<string> _fencedAccounts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Fence(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            Fence(new[] { accountName });
        }

        public void Fence(IEnumerable<string> accountNames)
        {
            var names = new List<string>();
            foreach (string accountName in accountNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(accountName))
                    continue;
                string normalized = accountName.Trim();
                if (!names.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    names.Add(normalized);
            }
            if (names.Count == 0)
                throw new ArgumentException("At least one account name is required.", nameof(accountNames));
            lock (_gate)
            {
                foreach (string name in names)
                    _fencedAccounts.Add(name);
            }
        }

        public void Release(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return;
            lock (_gate)
                _fencedAccounts.Remove(accountName.Trim());
        }

        public bool IsFenced(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return false;
            lock (_gate)
                return _fencedAccounts.Contains(accountName.Trim());
        }

        public bool TryExecute(
            string accountName,
            bool allowedWhileFenced,
            Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            lock (_gate)
            {
                if (!allowedWhileFenced
                    && !string.IsNullOrWhiteSpace(accountName)
                    && _fencedAccounts.Contains(accountName.Trim()))
                    return false;
                action();
                return true;
            }
        }
    }
}
