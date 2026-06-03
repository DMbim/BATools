using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.QA.Models;

public enum QaFixSafety
{
    SafeAutomatic,   // Can be applied in batch without user review
    UserConfirmed,   // Requires per-fix confirmation before applying
    Destructive,     // Irreversible; must be shown explicitly to user
    NotBatchable     // Has no automated fix path in current version
}