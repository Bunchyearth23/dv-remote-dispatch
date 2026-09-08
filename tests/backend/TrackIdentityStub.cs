namespace DvMod.RemoteDispatch
{
    public static class TrackIdentity
    {
        public static string Id(RailTrack track) => track.LogicTrack().ID.ToString();
    }
}
