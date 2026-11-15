# Qəbul modulu — spesifikasiya

Təsdiq: 2026-11-15. Qərarlar: loyallıq kartı = səviyyələr + bal; sosial güzəştlər = kataloq;
onlayn yazılış = D mərhələsində. Sıra: A → B → E → C → D → F.

## A. Vaxt və resurslar
- [x] A1. Xidmətə görə müddət (bir neçə xidmət → ardıcıl slotlar)
- [x] A1. Avadanlıq bronu (aparat məşğuldursa yazılış verilmir)
- [x] A1. Yazılış mənbəyi (telefon, registratura, onlayn, göndəriş, kampaniya)
- [x] A1. Ləğv səbəbləri kataloqu + kim ləğv etdi (xəstə / klinika / həkim)
- [x] A2. Kurs (seriya) qəbullar: tarixlər, toqquşma yoxlaması, alternativ, toplu ləğv
- [x] A3. Gözləmə siyahısı: boşalan vaxt → bildiriş / SMS (ilk yazılana təklif, imtina → növbəti, saat aralığı, müayinə müddəti, SMS linki ilə ləğv də)
- [x] A4. Avtomatik «gəlmədi» + sayğac + N-dən sonra xəbərdarlıq (Scheduling: AutoNoShowAfterMinutes, NoShowWarnAfter, NoShowWindowMonths; eyni gün «Gecikib gəldi»)
- [x] A5. Təqvim: gün / həftə, həkim və ya şöbə, sürüşdürüb köçürmə (iş saatı, fasilə, məzuniyyət, «indi» xətti, boş saata klik → yeni qəbul)
- [x] A6. Əlavə qəbul (araya salmaq): icazə + həkimə görə gündəlik limit (defolt 2; avadanlıq paylaşılmır)

## B. Həkim axını
- [x] B1. Göndəriş: həkim → başqa mütəxəssis / xidmət; resepsiyada siyahı; icra olunmayanlar (təcili, BXT-10, etibarlılıq, kənar göndəriş, vərəqə çapı; ləğv olunan qəbul → yenidən gözləyir)
- [x] B2. Kontrol qəbulu: «kontrol vaxtı çatanlar», SMS, N gün ərzində pulsuz / endirimli (Scheduling: FollowUpWindowDays, FollowUpDiscountPercent; xidmətdə «kontrol endirimi»; sətirdə ilkin qiymət / endirim / mənbə)

## E. Loyallıq və endirimlər
- [x] E1. Əl ilə endirim (faiz / məbləğ, səbəb məcburi, rol limiti, audit) — limit Rollar səhifəsində (Resepsiya/Kassir 10%, Admin 100%)
- [x] E2. Kampaniyalar və promo kodlar (müddət, xidmət / kateqoriya, istifadə limiti) — ümumi və xəstəyə limit, yalnız yeni xəstələr, kurs = 1 istifadə, ləğv limiti qaytarır
- [x] E3. Loyallıq kartı: səviyyələr (avtomatik keçid) + bal (yığılır / xərclənir), barkod — Kassada «Bonus bal», sabit səviyyə, bal düzəlişi, kart çapı
- [x] E4. Ailə kartı (bal və səviyyə ortaq)
- [x] E5. Sosial güzəştlər kataloqu (sənəd nömrəsi, etibarlılıq müddəti)
- [x] E6. Korporativ müştərilər (müqavilə endirimi, müqavilə müddəti)
- [x] E7. Hesablama: toplanmır, ən sərfəlisi; sətirdə ilkin qiymət / endirim / son qiymət / mənbə — formada canlı önbaxış

## C. Növbə
- [x] C1. Prioritet (hamilə, 65+, əlillik, təcili) — 65+ yaşa görə avtomatik; təcili hamıdan öndə
- [x] C2. Registratura və kassa növbəsi, pəncərələr (R-001 / KS-001; növbəti, təkrar çağır, xidmət edildi, gəlmədi)
- [x] C3. Səsli elan (Azərbaycan dilində; rəqəmlər sözlə: «Talon a on yeddi. Kabinet yüz bir.»)
- [x] C4. Kiosk: FİN / loyallıq kartı / SMS QR ilə gəldim, talon çapı (vaxt pəncərəsi, ad gizli, «Kiosk» rolu)

## D. Xəstə tərəfi
- [x] D1. Onlayn yazılış portalı /online (SMS kodu, limitlər, qoruma) — kod heşlə, 5 dəq, 5 cəhd, saatda 3 kod; FİN + kartdakı telefon; 3 aktiv onlayn qəbul, 30 gün irəli, 2 saat əvvəl; IP limiti, bot tələsi
- [x] D2. Linklə vaxt dəyişmə (eyni həkim və xidmətlər, ən çox 2 dəfə; boşalan vaxt gözləmə siyahısına)
- [x] D3. Qəbuldan sonra qiymətləndirmə (1–5, şərh; SMS ilə dəvət, 14 gün; rəhbərlik üçün «Qiymətləndirmələr»)

## F. Hesabatlar
- [ ] F1. Doluluq, gəlməmə, ləğv səbəbləri, yazılışdan qəbula müddət, yeni / təkrar xəstə,
      mənbə, kampaniya və endirim effekti, orta gözləmə
