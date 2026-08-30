The A330's nav and logo light write now hands its two indexed light events both of the
operands they pop — the index as well as the value. It had pushed only the value, copied
from FlyByWire's own A339X preset procedure file, which left each event taking whatever
happened to be left on the calculator stack as its light index. The write now uses the
same index-then-value form the A320 definition has always used, and a test pins the
operand count so the one-operand shape cannot come back. Unreleased: the A330 First
Officer ships in this same pull request.
