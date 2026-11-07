namespace MSFSBlindAssist.Services;

// The fields of the captain's DCDU: the part of RemoteData the DCDU feature alone reads, and the message nested in it.
// Measured on 3.0.0.7613 (2026-11-03): the empty DCDU sent field 186 with every line present as a string, the header,
// the message lines and the key labels empty and two of the info lines 14 spaces.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int DcduCapt = 186;            // dcduCapt: nested DcduContent, the captain's DCDU
    }

    /// <summary>
    /// DcduContent, what the captain's DCDU draws (<see cref="Remote.DcduCapt"/>): the header, five message lines, three
    /// info lines and the four soft key labels, all as channel text. Field 1, PrintFrame (a varint bool), is not read.
    /// </summary>
    public static partial class DcduContent
    {
        public const int HeaderLeft = 2;            // HeaderLeft: string (channel text)
        public const int HeaderRight = 3;           // HeaderRight: string (channel text)
        public const int Msg1 = 4;                  // Msg1: string (channel text), the first message line
        public const int Msg2 = 5;                  // Msg2: string (channel text)
        public const int Msg3 = 6;                  // Msg3: string (channel text)
        public const int Msg4 = 7;                  // Msg4: string (channel text)
        public const int Msg5 = 8;                  // Msg5: string (channel text), the last message line
        public const int Info1 = 9;                 // Info1: string (channel text)
        public const int Info2 = 10;                // Info2: string (channel text); 14 spaces on the empty DCDU
        public const int Info3 = 11;                // Info3: string (channel text); 14 spaces on the empty DCDU
        public const int Lsk1L = 12;                // Lsk1L: string (channel text), the label of left key 1
        public const int Lsk2L = 13;                // Lsk2L: string (channel text), the label of left key 2
        public const int Lsk1R = 14;                // Lsk1R: string (channel text), the label of right key 1
        public const int Lsk2R = 15;                // Lsk2R: string (channel text), the label of right key 2
    }
}
