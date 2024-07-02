namespace KafkaTool;

public static class Utils
{
    public static string GetTopicName(int i)
    {
        return $"my-dotnet-topic-{i}";
    }
}