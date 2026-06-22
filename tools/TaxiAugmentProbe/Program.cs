using MSFSBlindAssist.Services.TaxiAugment;
int failures = 0;
void Check(bool ok, string label){ Console.WriteLine((ok?"PASS ":"FAIL ")+label); if(!ok) failures++; }
// tasks below add Check(...) calls.
Console.WriteLine(failures==0 ? "ALL PASS" : $"{failures} FAILURES");
return failures==0 ? 0 : 1;
