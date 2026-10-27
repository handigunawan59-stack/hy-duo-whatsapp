namespace DuoDesk;

internal static class ConnectionAdvice
{
    public static string ForError(string? error) => error switch
    {
        "Disconnected" or "InternetDisconnected" => "Jaringan terputus — periksa koneksi",
        "Timeout" => "Koneksi habis waktu — periksa koneksi",
        "CannotConnect" or "ConnectionAborted" or "ConnectionReset" => "Tidak dapat terhubung — periksa koneksi",
        _ => "Halaman bermasalah — lihat bantuan koneksi"
    };
}
