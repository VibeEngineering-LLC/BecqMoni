# #BM-21 (RestHarnessBm21.cs): source mutations of the firmware version warning, in the format of rest_suite.MUTATIONS
# (id, description, [(file, old text, new text)], the scenario that must be red, full run). On the good code no bm21 scenario is red.
FW = "AtomSpectraFirmware.cs"
SES = "AtomSpectraRestSession.cs"
READ = "AtomSpectraFirmware.ReadVersion(client, FirmwareReadMs)"
MUTATIONS = [
    ("bm21-string-compare", "the firmware versions are compared as strings (1.10.0 < 1.2.39)",
     [(FW, "if (major != RecMajor)", "if (string.CompareOrdinal(version.Trim(), RecommendedVersion) != 0) return string.CompareOrdinal(version.Trim(), RecommendedVersion) < 0;\n            if (major != RecMajor)")], "bm21-parse", True),
    ("bm21-threshold-38", "the threshold is 1.2.38 instead of 1.2.39",
     [(FW, 'public const string RecommendedVersion = "1.2.39";', 'public const string RecommendedVersion = "1.2.38";')], "bm21-start-low", True),
    ("bm21-hint-on-success", "the firmware is read at every Start, so the hint is there when the Start succeeds",
     [(SES, "lastProbe = second;", "lastProbe = second;\n            FirmwareVersion = " + READ + ";")], "bm21-start-ok-low", True),
    ("bm21-hint-unknown", "the hint is added when the version is unknown (?)",
     [(FW, "if (below != true)", "if (below == false)")], "bm21-start-fail-unknown", True),
    ("bm21-block-start", "the Start is refused on a firmware below 1.2.39 instead of a warning",
     [(SES, "lastProbe = second;", "if (AtomSpectraFirmware.IsBelowRecommended(" + READ + ") == true) { Failure = AtomSpectraRestFailure.Unreachable; return AtomSpectraTcpBeginOutcome.ConnectFailed; }\n            lastProbe = second;")], "bm21-start-ok-low", True),
]
