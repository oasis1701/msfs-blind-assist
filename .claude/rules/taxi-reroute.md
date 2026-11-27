---
paths:
  - "MSFSBlindAssist/Navigation/RerouteOffer.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RerouteOffer*.cs"
---
# Taxi reroute offer rules

- [RTE-40] With "ask before switching" on, `TryRecalculateRoute` OFFERS a route it would adopt (`RerouteOffer`) and changes nothing else; only the pilot's Continue adopts it, recalculated from the live position; one offer per excursion, withdrawn silently back on the route line or on leaving Taxiing. Full: docs/invariants/taxi-reroute.md#rte-40
