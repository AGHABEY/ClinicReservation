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

Sonra brauzerdə açın: **http://localhost:8090**

- Tətbiq başlayanda migration-lar avtomatik tətbiq olunur və ilkin data (rollar,
  admin istifadəçisi, statuslar) seed edilir.
- PostgreSQL datası `pgdata` adlı Docker volume-da saxlanılır.

### Portlar (toqquşmanın qarşısını almaq)

Host portları dəyişdirilə bilər — `.env` faylı yaradın (`.env.example`-dən kopyalayın)
və ya əmrlə verin:

| Dəyişən | Default (host) | Konteyner |
|---------|:--------------:|:---------:|
| `WEB_PORT` | `8090` | `8080` |
| `DB_PORT`  | `5433` | `5432` |

```bash
WEB_PORT=9000 DB_PORT=5544 docker compose up --build
```

> DB host portu standart `5432` deyil `5433`-dür ki, maşınınızdakı lokal PostgreSQL
> ilə toqquşmasın.

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

- **İdarə paneli (Dashboard)** — ümumi statistikalar, gəlir göstəriciləri və son rezervasiyalar
- **Kassa** — rezervasiyalar üzrə ödənişlər (nağd/kart), qismən ödəniş, ödəniş tarixçəsi
- **Hesabatlar** — tarix filtri ilə detallı maliyyə hesabatı: ümumi gəlir, nağd/kart
  bölgüsü, günlük gəlir qrafiki, müayinə növləri üzrə bölgü və əməliyyat siyahısı
- **Rezervasiyalar** — əlavə etmə (çoxlu müayinə seçimi), siyahı, detallar, təsdiq
- **Müayinələr** — müayinə növləri üçün CRUD (ad, təsvir, qiymət)
- **İstifadəçilər** — istifadəçi yaratma və rol təyinetmə

## 📁 Struktur

```
Controllers/   Home, Account, Reservation, ExaminationType
Models/        Reserve, ExaminationType, ReserveStatus, AppUser, ...
Data/          AppDbContext, SeedData (migration + seed)
ViewModels/    Account, UserList, AdminRole, Dashboard
Views/         Modern dizaynlı Razor səhifələri
wwwroot/css/   site.css (custom tema)
Migrations/    PostgreSQL migration-ları
Dockerfile, docker-compose.yml
```
