# MedClinic — Klinika Rezervasiya Sistemi

ASP.NET Core 8 MVC ilə hazırlanmış klinika rezervasiya / növbə idarəetmə paneli.
Rezervasiyalar, müayinə növləri və istifadəçilər bir paneldən idarə olunur.

## ✨ Texnologiyalar

| Komponent | Detal |
|-----------|-------|
| Framework | ASP.NET Core 8.0 MVC |
| ORM | Entity Framework Core 8 (Code-First, migration-on-startup) |
| Verilənlər bazası | **PostgreSQL 16** (Npgsql) |
| Autentifikasiya | ASP.NET Core Identity (rol əsaslı) |
| UI | Custom modern dizayn (Bootstrap Icons, Inter font) |
| Konteyner | **Docker + Docker Compose** |

## 🚀 Docker ilə işə salma (tövsiyə olunan)

Tək əmrlə həm verilənlər bazası, həm də veb tətbiq qalxır:

```bash
docker compose up --build
```

Sonra brauzerdə açın: **http://localhost:8080**

- Tətbiq başlayanda migration-lar avtomatik tətbiq olunur və ilkin data (rollar,
  admin istifadəçisi, statuslar) seed edilir.
- PostgreSQL datası `pgdata` adlı Docker volume-da saxlanılır.

Dayandırmaq üçün:

```bash
docker compose down          # konteynerləri dayandırır
docker compose down -v       # bazanı da silir
```

## 🔑 Default giriş

| İstifadəçi adı | Şifrə |
|----------------|-------|
| `admin` | `admin123` |

## 💻 Lokal işə salma (Docker olmadan)

Tələblər: .NET 8 SDK + işləyən PostgreSQL.

1. PostgreSQL-də baza yaradın və `appsettings.json`-da connection string-i uyğunlaşdırın:
   ```
   Host=localhost;Port=5432;Database=ClinicReservation;Username=postgres;Password=postgres
   ```
2. Tətbiqi işə salın (migration-lar avtomatik tətbiq olunur):
   ```bash
   dotnet run
   ```

> Bağlantı sətrini environment dəyişəni ilə də verə bilərsiniz:
> `ConnectionStrings__ClinicReservation`

## 🧩 Əsas funksiyalar

- **Xəstə kartı** — hər xəstə FİN ilə bir dəfə qeyd olunur; vizit tarixçəsi, borc, qeydlər
  (məs. allergiya). Rezervasiya formasında FİN yazanda məlumatlar avtomatik dolur
- **Həkimlər** — ixtisas, şöbə, kabinet, qəbul müddəti, həftəlik iş qrafiki (fasilə ilə),
  məzuniyyətlər; həkimə sistem istifadəçisi bağlana bilər (yalnız öz qəbullarını görür)
- **Şöbə və kabinetlər** — eyni kabinetdə iki həkimin qəbulu üst-üstə düşə bilməz
- **Rezervasiyalar** — həkimin qrafikinə görə boş saatlar; status axını:
  Gözləyir → Təsdiqləndi → Gəldi → Qəbulda → Tamamlandı (+ Gəlmədi, Ləğv)
- **Kassa** — ödəniş, qismən ödəniş, geri qaytarma; ləğv və «gəlmədi» rezervasiyalar borc yaratmır
- **Hesabatlar** — xalis gəlir, nağd/kart, günlük qrafik, müayinə və həkim üzrə statistika
  («gəlmədi» faizi daxil)
- **Rollar və icazələr** — Admin, İşçi, Resepsiya, Kassir, Həkim, Rəhbərlik, Laborant; hər rolun icazələri
  «Rollar və icazələr» səhifəsində seçilir (Admin həmişə hər şeyə icazəlidir)
- **Audit jurnalı** — kim nə vaxt nə etdi: girişlər, xəstə kartına baxış, rezervasiya və
  status dəyişiklikləri, ödənişlər, istifadəçi və icazə dəyişiklikləri
- **İstifadəçilər** — yaratma, rol, şifrə sıfırlama, silmə; öz şifrəsini dəyişmə
- **Elektron növbə** — xəstə «Gəldi» qeyd olunanda talon alır (məs. `K-007`; hərf şöbədə
  seçilir), talon 80 mm çekdə çap olunur. Gözləmə zalı üçün tam ekran **növbə ekranı**:
  çağırılan talon → kabinet, növbədə olanlar, səs siqnalı; xəstə adı göstərilmir
- **Həkimin iş masası** — günün növbəsi, gözləmə vaxtı, «Növbəti xəstəni çağır»,
  cari xəstə (allergiya xəbərdarlığı ilə), qəbula başla / bitir, orta gözləmə vaxtı
- **Tibbi kart** — hər vizit üçün şikayət, anamnez, obyektiv müayinə, BXT-10 kodu ilə diaqnoz,
  müalicə, resept, tövsiyələr, kontrol qəbulu; A4 formatda çap / PDF. Allergiya və xroniki
  xəstəliklər xəstə kartında qırmızı xəbərdarlıq kimi görünür. Həkim yalnız öz xəstəsinə
  qeyd yaza bilər; hər baxış və dəyişiklik audit jurnalına düşür
- **Laboratoriya** — analiz kataloqu (kod, vahid, nümunə, kişi/qadın norması, kritik dəyərlər,
  31 standart analiz bir kliklə), analizlər müayinəyə bağlanır və kassadan ödənilir. Laborant
  «Nümunə götür» deyəndə hər nümunə növü üçün barkod (58×40 mm etiket) yaranır; nəticə yazılarkən
  normadan kənar dəyərlər ↑/↓, kritik dəyərlər qırmızı göstərilir və ayrıca təsdiq tələb edir;
  əvvəlki nəticə yanında görünür. Təsdiqdən sonra A4 blank, xəstəyə SMS və doğum tarixi ilə
  qorunan onlayn nəticə səhifəsi. Həkim qəbul zamanı analiz təyin edir və nəticəni tibbi
  qeyd ekranında görür. Təsdiqi geri almaq yalnız səbəb yazmaqla mümkündür (audit)
- **SMS xatırlatma** — qəbuldan 24 saat əvvəl avtomatik SMS; içindəki linklə xəstə girişsiz
  «Gələcəyəm» və ya «Gələ bilməyəcəyəm» seçir. SMS jurnalı, əl ilə göndəriş, sakit saatlar

## 🚦 İlk quraşdırma

1. «Şöbə və kabinetlər» səhifəsində şöbələri və kabinetləri əlavə edin.
2. «Həkimlər» səhifəsində həkimləri və onların həftəlik qrafikini qurun.
3. «Müayinələr»də müayinələri şöbələrə bağlayın (istəyə bağlı).
4. «İstifadəçilər»də işçilərə rol verin, həkimlərə öz loginini bağlayın.
5. «Analiz kataloqu»nda standart analizləri yükləyin, normaları öz laboratoriyanıza görə
   yoxlayın və «Müayinələr»də analiz müayinələrini (məs. «Ümumi qan analizi») analizlərə bağlayın.
6. Şöbələrə talon hərfi verin (məs. Kardiologiya → `K`) və gözləmə zalındakı TV-də
   «Növbə ekranı»nı açın (növbə ekranı icazəsi olan ayrıca istifadəçi ilə).

## 📱 SMS sazlamaları

Default olaraq SMS **test rejimindədir**: heç nə göndərilmir, mesajlar yalnız
«SMS xatırlatmaları» səhifəsindəki jurnala yazılır. Real göndəriş üçün `appsettings.json` → `Sms`:

```json
"Sms": {
  "Provider": "Http",
  "PublicBaseUrl": "https://clinic.example.az",
  "Http": {
    "Url": "https://provayder.az/send?login=...&key=...&msisdn={phone}&text={text}",
    "SuccessContains": ""
  }
}
```

- `{phone}` → `994501234567` formatında nömrə, `{text}` → mesaj (URL-encode olunur).
- `PublicBaseUrl` — xəstənin açacağı təsdiq linkinin ünvanı (internetdən açılmalıdır).
- `ReminderHoursBefore` (24), `MinLeadHours` (3), `QuietStart`/`QuietEnd` (21:00–09:00),
  `AsciiOnly` (ə, ş, ç… latın hərfi ilə əvəz olunur — 1 SMS 160 simvol olur).
- Provayder açarlarını repozitoriyaya yazmayın: environment dəyişəni ilə verin, məs.
  `Sms__Http__Url`.

## 🕘 Default iş saatı

Yeni həkim əlavə edilərkən qrafik bu dəyərlərlə doldurulur (sonra hər həkim üçün ayrıca
dəyişdirilir). `appsettings.json` → `Clinic`:

```json
"Clinic": {
  "WorkStart": "09:00",
  "WorkEnd": "18:00",
  "SlotMinutes": 30
}
```

Qəbul saatları həkimin öz qrafikindən hesablanır: bir həkim eyni anda bir xəstə qəbul edir,
bir kabinetdə eyni anda bir qəbul olur.

## 📁 Struktur

```
Controllers/   Home, Account, Reservation, Patient, Doctor, Department, Role, Audit,
               ExaminationType, Kassa, Report
Authorization/ İcazə əsaslı avtorizasiya ([HasPermission])
Models/        Reserve, ExaminationType, ReserveStatus, Payment, AppUser, ClinicOptions, ...
Services/      SlotService (həkim qrafikinə görə saatlar), ReserveFlow (status axını),
               AuditService
Helpers/       Fmt (Azərbaycan formatında tarix/məbləğ)
Data/          AppDbContext, SeedData (migration + seed)
ViewModels/    Form, Kassa, Report, Dashboard, Account, ...
Views/         Modern dizaynlı Razor səhifələri
wwwroot/css/   site.css (custom tema)
Migrations/    PostgreSQL migration-ları
Dockerfile, docker-compose.yml
```
