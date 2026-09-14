# 🛠️ AyazTek Servis

<p align="center">
  <b>Ayaz Teknoloji için geliştirilen teknik servis kayıt ve takip uygulaması.</b><br>
  Servis kabulünden teknik kontrole, teslimden raporlamaya kadar tek panel.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white&style=flat-square">
  <img src="https://img.shields.io/badge/ASP.NET%20Core-MVC-512BD4?logo=dotnet&logoColor=white&style=flat-square">
  <img src="https://img.shields.io/badge/EF%20Core-SQLite-003B57?logo=sqlite&logoColor=white&style=flat-square">
  <img src="https://img.shields.io/badge/Mail-MailKit-185C72?style=flat-square">
</p>

---

## 🎯 Projenin Amacı

AyazTek Servis; teknik servise gelen cihazların kayıt, kontrol, durum takibi, teslim, raporlama ve kullanıcı işlemlerini tek uygulamada yönetmek için geliştirilmiştir.

Amaç:

- Arızalı ürünleri standart biçimde kaydetmek
- Otomatik servis takip numarası üretmek
- Teknik kontrol ve servis sürecini izlemek
- Garanti ve servis ücretini iç sistemde takip etmek
- KDV dahil / KDV hariç fiyat bilgisini kaydetmek
- Müşteriye servis kayıt ve teslim belgesi sunmak
- Servis durum değişikliklerinde e-posta bilgilendirmesi yapmak
- Kullanıcı, oturum, log ve yedek yönetimini merkezi hale getirmek

---

## 🚀 Özellikler

- 📋 Arızalı ürün kayıt sistemi
- 🔢 Aylık otomatik servis takip numarası
- 🔧 Teknik kontrol ve servis durum yönetimi
- 💰 Servis ücreti + para birimi + KDV durumu
- 🛡️ Garanti / garantisiz takibi
- 🧩 Parça ve seri numarası takibi
- 📦 Teslim kuyruğu ve ürün teslim işlemleri
- 🖨️ A4 servis kayıt ve teslim formları
- 📊 Excel ve yazdırılabilir raporlar
- 🔔 İş günü bazlı servis uyarıları
- ✉️ Kurumsal şablonlu SMTP müşteri e-postaları ve haftalık rapor gönderimi
- 🗂️ Soft delete, çöp kutusu ve geri yükleme
- 🧾 Kullanıcı bazlı, eski → yeni değerleri gösteren ayrıntılı audit logları
- 👤 Yönetici ve kullanıcı yetkilendirmesi
- 🔐 PBKDF2-SHA256 parola hashleme
- 🔑 Süreli ve tek kullanımlık şifre sıfırlama tokenları
- 🖥️ Aktif oturum yönetimi
- 💾 SQLite yedekleme sistemi
- 🌓 Kullanıcı ve sistem tema tercihleri

---

## 🧠 Sistem Nasıl Çalışır?

### 1️⃣ Servis Kaydı

Yeni cihaz kaydında ürün, müşteri, gönderilen servis, arıza, aksesuar ve parça bilgileri alınır. Sistem takip numarasını otomatik oluşturur.

### 2️⃣ Teknik Kontrol

Kontrol ekranında:

- Servis durumu
- Garanti durumu
- Servis ücreti
- Para birimi
- KDV dahil / KDV hariç seçimi
- Yapılan işlem
- Teknik kontrol notu

yönetilir.

Fiyat bilgisi yalnızca iç servis ekranlarında görünür. Müşteriye verilen **Servis Kayıt Formu** ve **Ürün Teslim Formu** üzerinde servis ücreti gösterilmez.

### 3️⃣ Teslim

Ürün `Teslime Hazır` durumuna geldiğinde teslim işlemi tamamlanır ve müşteriye tek sayfalık A4 teslim formu oluşturulur.

### 4️⃣ Bildirim ve E-posta

Önemli servis durumlarında müşteri bilgilendirme e-postaları gönderilebilir. SMTP parolası kaynak kodda tutulmaz.

### 5️⃣ Yönetim

Admin Merkezi üzerinden kullanıcılar, aktif oturumlar, audit logları, hata kayıtları, yedekler, sistem ayarları ve e-posta işlemleri yönetilir.

---

## 🛠️ Teknolojiler

- .NET 10
- ASP.NET Core MVC
- Entity Framework Core 10
- SQLite
- Bootstrap 5
- MailKit / MimeKit
- ASP.NET Core Cookie Authentication
- ASP.NET Core Data Protection

---

## 📂 Proje Yapısı

```text
AyazTekServis/
├── Controllers/
├── Models/
├── Services/
├── ViewModels/
├── Views/
│   ├── Account/
│   ├── Admin/
│   ├── Home/
│   ├── Notifications/
│   ├── Service/
│   └── Shared/
├── wwwroot/
│   ├── css/
│   ├── images/
│   ├── js/
│   └── lib/
├── Program.cs
├── appsettings.json
└── AyazTekServis.csproj
```

---

## 🔐 Güvenlik

Bu repo içinde bulunmaması gerekenler:

- ❌ Gerçek `AyazTekServis.db`
- ❌ SQLite `-wal` ve `-shm` dosyaları
- ❌ SMTP parolası
- ❌ `.env` / secret dosyaları
- ❌ Production sertifikaları ve private key dosyaları
- ❌ `Backups/` içeriği
- ❌ Visual Studio `.vs/`, `bin/`, `obj/` dosyaları

Hassas bilgiler environment variable, User Secrets veya uygulamanın korumalı sistem ayarları üzerinden yönetilmelidir.

---

## ⚙️ Kurulum

### Gereksinimler

- .NET 10 SDK
- Windows, Linux veya macOS

### Projeyi çalıştırma

```bash
git clone <repo-url>
cd AyazTekServis/AyazTekServis
dotnet restore
dotnet run
```

Development ortamında varsayılan adres:

```text
http://127.0.0.1:5140
```

SQLite veritabanı bulunmuyorsa uygulama gerekli tablo ve kolonları başlangıçta oluşturur.

### SMTP

SMTP parolasını repoya yazmayın. Örneğin User Secrets:

```bash
dotnet user-secrets set "Smtp:Password" "YOUR_SMTP_PASSWORD"
```

Dışarıdan erişilecek şifre sıfırlama bağlantıları için Admin → Sistem Ayarları bölümünde geçerli bir **Dış Erişim Adresi** tanımlanmalıdır.

---

## 🗃️ İlk Yönetici

Temiz veritabanında kayıt olan hesaplar güvenlik nedeniyle aktif kullanıcı ve admin olarak başlamaz. İlk yönetici hesabı güvenli kurulum sırasında veritabanında yetkilendirilmelidir. Uygulamayı internete açmadan önce bu işlemi tamamlayın.

---

## 🌐 Yayınlama

Windows sunucuda IIS veya bir VPS üzerinde çalıştırılabilir.

```bash
dotnet publish ./AyazTekServis.csproj -c Release -o ./publish
```

Production ortamında gerçek alan adı + HTTPS kullanılması önerilir. Development tunnel adresleri kalıcı production adresi olarak kullanılmamalıdır.

---

## 📦 GitHub'a Yüklemeden Önce

`.gitignore` dosyasının aktif olduğundan emin olun ve:

```bash
git status
```

çıktısında veritabanı, yedek, parola veya yerel geliştirme dosyası olmadığını kontrol edin.

---

## 📝 Not

Bu proje Ayaz Teknoloji teknik servis süreçlerinin günlük kullanımını kolaylaştırmak amacıyla geliştirilmiştir.

Made by Selçuk Şahin

## ⚖️ Lisans
Bu proje GNU General Public License v3.0 ile lisanslanmıştır. zyixcode tarafından geliştirilen bu projeyi, lisans koşullarına uyarak özgürce kullanabilirsiniz.
