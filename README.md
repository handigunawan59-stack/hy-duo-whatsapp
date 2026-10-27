# HY Duo WhatsApp

Aplikasi Windows untuk memakai dua sesi WhatsApp Web dalam satu jendela. Proyek ini dibuat secara independen dan tidak berafiliasi dengan WhatsApp atau Meta.

## Fitur

- Dua sesi terpisah untuk akun Pribadi dan Usaha, masing-masing dengan penyimpanan browser sendiri.
- Tampilan berdampingan atau satu akun per tab.
- Notifikasi Windows dan suara saat aplikasi dan kedua sesi aktif; kemampuan notifikasi bergantung pada WhatsApp Web, WebView2, dan pengaturan Windows.
- Folder unduhan terpisah, ikon baki sistem, dan kunci tampilan menggunakan PIN.
- Satu instance aplikasi untuk folder data yang sama.

WhatsApp Web menangani login melalui QR, pesan, unggahan, dan unduhan. Aplikasi tidak menyediakan pintasan penerusan otomatis antar-akun.

## Persyaratan dan build

Windows 10/11 64-bit, Microsoft Edge WebView2 Runtime, dan .NET SDK 8 diperlukan untuk membangun proyek. Pengguna installer tidak perlu memasang .NET SDK. Jalankan dari direktori repositori:

```powershell
dotnet restore duodesk/DuoDesk.csproj
dotnet publish duodesk/DuoDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Untuk membuat installer, pasang NSIS kemudian jalankan `powershell -ExecutionPolicy Bypass -File duodesk/installer/build-installer.ps1`. Script ini membuat aplikasi mandiri dan installer pada `duodesk/dist`.

Pemeriksaan otomatis dapat dijalankan dengan `dotnet run --project duodesk-tests`, `dotnet run --project pinlock-tests`, dan `dotnet run --project notification-bridge-tests`.

## Data dan privasi

Sesi login, pengaturan, dan PIN berada di folder data lokal pengguna (`%LOCALAPPDATA%\DuoDesk`) dan tidak termasuk dalam repositori. PIN mengunci tampilan, bukan mengenkripsi sesi atau file. Menghapus instalasi mempertahankan data sesi kecuali dihapus secara manual. Tinjau pengaturan notifikasi jika isi pesan tidak ingin ditampilkan pada layar.

Pengembang tidak dapat menjamin notifikasi ketika aplikasi dihentikan, komputer tidur, koneksi terputus, atau WhatsApp Web mengubah perilakunya. Microsoft WebView2 tidak menyediakan web push notifications. WhatsApp dapat meminta login ulang sewaktu-waktu.
