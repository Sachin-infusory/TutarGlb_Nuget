public abstract class LogProvider
{
    protected abstract string GetDecryptionKey();
    public string GetLog()
    {
        return GetDecryptionKey();
    }
}

public class GlbLogProvider : LogProvider
{
    protected override string GetDecryptionKey()
    {
        return GetLog("YmYzYzE5OWMyNDcwY2I0NzdkOTA3YjFlMDkxN2MxN2I=");
    }

    private string GetLog(string encoded)
    {
        byte[] keyBytes = Convert.FromBase64String(encoded);
        return System.Text.Encoding.UTF8.GetString(keyBytes);
    }
}