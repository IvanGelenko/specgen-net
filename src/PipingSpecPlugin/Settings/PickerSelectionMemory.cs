using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Settings
{
    /// <summary>
    /// What the picker remembers between WRITESPEC runs for one filter list
    /// (statuses, categories or lines): the checked keys ("selected") and
    /// every key the user has SEEN ("known"). A key that is known but not
    /// selected was unchecked on purpose; a key that is not known is new and
    /// starts checked (SpecPickerWindow.PopulateFilters).
    ///
    /// The picker only shows the chips of ONE scope. Saving its view as the
    /// whole memory broke both directions whenever that view was a part of
    /// the project (scope "dwg" — the «Текущий чертёж» button, or WRITESPEC
    /// with the drawing radio):
    ///  • "known" used to be the project-wide fact list, so lines/statuses of
    ///    OTHER drawings — never shown, never ticked — were stored as known
    ///    but unselected and came up UNCHECKED in the next «Весь проект» run:
    ///    their parts silently left the spec unless the user noticed;
    ///  • after a single-drawing scan "known" shrank to that drawing, so a
    ///    line the user had deliberately excluded elsewhere was forgotten and
    ///    came back CHECKED in the next project run.
    /// For a partial view the keys that were not shown now keep their
    /// previous state; keys never seen stay unknown (new → checked). A full
    /// project view replaces the memory, exactly as before, so keys of other
    /// projects do not pile up forever.
    /// </summary>
    public static class PickerSelectionMemory
    {
        public static (string[] Selected, string[] Known) Merge(
            string[] oldSelected, string[] oldKnown,
            IEnumerable<string> shown, IEnumerable<string> picked, bool partialView)
        {
            var shownList = (shown ?? Enumerable.Empty<string>())
                .Where(k => k != null).Distinct(StringComparer.Ordinal).ToList();
            var selected = (picked ?? Enumerable.Empty<string>())
                .Where(k => k != null).Distinct(StringComparer.Ordinal).ToList();
            var known = new List<string>(shownList);
            if (partialView && oldKnown != null)
            {
                var shownSet = new HashSet<string>(shownList, StringComparer.Ordinal);
                var pickedSet = new HashSet<string>(selected, StringComparer.Ordinal);
                // null = "everything selected" (no explicit list saved yet).
                var oldSel = oldSelected == null ? null : new HashSet<string>(oldSelected, StringComparer.Ordinal);
                foreach (var key in oldKnown.Where(k => k != null).Distinct(StringComparer.Ordinal))
                {
                    if (shownSet.Contains(key)) continue;
                    known.Add(key);
                    if ((oldSel == null || oldSel.Contains(key)) && pickedSet.Add(key))
                        selected.Add(key);
                }
            }
            return (selected.ToArray(), known.ToArray());
        }
    }
}
