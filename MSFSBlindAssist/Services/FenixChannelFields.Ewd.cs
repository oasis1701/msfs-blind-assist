namespace MSFSBlindAssist.Services;

// The fields of the E/WD window (the upper ECAM): the part of RemoteData the E/WD feature alone reads. The engines (129),
// with their thrust limit (129.8), the weight unit (154) and the two text columns (173, 174) are declared in
// FenixChannelFields.cs, and are not repeated here.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int FlapEcamState = 152;       // flapEcamState: string (channel text), the E/WD's flap and slat text
    }
}
