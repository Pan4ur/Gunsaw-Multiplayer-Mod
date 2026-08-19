using System;
using System.Collections.Generic;

namespace DiscordIPC
{
    public sealed class Draft
    {
        public object DraftId { get; set; }
        public string Details { get; set; }
        public string State { get; set; }
        public PresencePair LargeImage { get; set; }
        public PresencePair SmallImage { get; set; }
        public PresencePair Button1 { get; set; }
        public PresencePair Button2 { get; set; }
        public PresenceParty Party { get; set; }
        public string JoinSecret { get; set; }

        public Draft()
        {
            DraftId = 0;
        }
    }

    internal sealed class DraftService
    {
        private readonly object _sync = new object();
        private readonly List<Draft> _drafts = new List<Draft>();
        private bool _autoRoll;
        private int _currentId;

        public void Save(Draft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            lock (_sync) _drafts.Add(draft);
        }

        public Draft Find(object draftId)
        {
            lock (_sync)
            {
                for (int i = 0; i < _drafts.Count; i++)
                {
                    if (object.Equals(_drafts[i].DraftId, draftId)) return _drafts[i];
                }
            }
            return null;
        }

        public void StartRolling()
        {
            lock (_sync)
            {
                if (_drafts.Count == 0)
                {
                    _autoRoll = false;
                    return;
                }

                _currentId = 0;
                _autoRoll = true;
            }
        }

        public void StopRolling()
        {
            lock (_sync) _autoRoll = false;
        }

        public Draft NextRollingDraft()
        {
            lock (_sync)
            {
                if (!_autoRoll || _drafts.Count == 0) return null;
                if (_currentId >= _drafts.Count) _currentId = 0;

                Draft result = _drafts[_currentId];
                _currentId = (_currentId + 1) % _drafts.Count;
                return result;
            }
        }
    }
}
