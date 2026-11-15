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
- [ ] E1. Əl ilə endirim (faiz / məbləğ, səbəb məcburi, rol limiti, audit)
- [ ] E2. Kampaniyalar və promo kodlar (müddət, xidmət / kateqoriya, istifadə limiti)
- [ ] E3. Loyallıq kartı: səviyyələr (avtomatik keçid) + bal (yığılır / xərclənir), barkod / QR
- [ ] E4. Ailə kartı
- [ ] E5. Sosial güzəştlər kataloqu (sənəd nömrəsi, etibarlılıq müddəti)
- [ ] E6. Korporativ müştərilər (müqavilə endirimi)
- [ ] E7. Hesablama: toplanmır, ən sərfəlisi; sətirdə ilkin qiymət / endirim / son qiymət / mənbə

## C. Növbə
- [ ] C1. Prioritet (hamilə, 65+, əlillik, təcili)
- [ ] C2. Registratura və kassa növbəsi, pəncərələr
- [ ] C3. Səsli elan (Azərbaycan dilində)
- [ ] C4. Kiosk: FİN / QR ilə gəldim, talon çapı

## D. Xəstə tərəfi
- [ ] D1. Onlayn yazılış portalı (SMS kodu, limitlər, qoruma)
- [ ] D2. Linklə vaxt dəyişmə
- [ ] D3. Qəbuldan sonra qiymətləndirmə (1–5, şərh)

## F. Hesabatlar
- [ ] F1. Doluluq, gəlməmə, ləğv səbəbləri, yazılışdan qəbula müddət, yeni / təkrar xəstə,
      mənbə, kampaniya və endirim effekti, orta gözləmə
