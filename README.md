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

- **Hesabatlar (Dashboard)** — ümumi statistikalar və son rezervasiyalar
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
