When you load a PMDG 737 or 777, or pick one from the Aircraft menu, MSFS Blind Assist now
checks whether PMDG's own SDK data broadcast is turned on for that aircraft — without it, the
PMDG CDU window in this app (and any other third-party CDU display) can't show anything. If
it's off, you're asked whether to turn it on; saying yes backs up the aircraft's configuration
file, adds the required setting, and reminds you that Microsoft Flight Simulator needs to be
closed from its Main Menu — not force-closed with Alt+F4 — and restarted before the change
takes effect. If the configuration file can't be found or can't be read, you're told that too,
rather than the check failing silently.
