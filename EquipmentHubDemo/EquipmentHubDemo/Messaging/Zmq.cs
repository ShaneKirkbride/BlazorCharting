namespace EquipmentHubDemo.Messaging;

public static class Zmq
{
    // Wire ports for the XSUB/XPUB proxy hosted in-process.
    public const string XSubBind = "tcp://*:5556"; // publishers connect here
    public const string XPubBind = "tcp://*:5557"; // subscribers connect here

    public const string PubConnect = "tcp://127.0.0.1:5556";
    public const string SubConnect = "tcp://127.0.0.1:5557";

    public const string TopicMeasure = "measure";
    public const string TopicFiltered = "filtered";
}
