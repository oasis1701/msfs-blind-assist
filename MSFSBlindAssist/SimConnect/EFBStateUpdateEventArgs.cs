namespace MSFSBlindAssist.SimConnect
{
    public class EFBStateUpdateEventArgs : EventArgs
    {
        public string Type { get; set; } = "";
        public Dictionary<string, string> Data { get; set; } = new();
    }
}
