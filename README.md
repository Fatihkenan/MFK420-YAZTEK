# MFK420-YAZTEK 🎙️📖

**Yapay Zekâ Destekli Sesli Okuma Analiz Platformu**

Bu depo, İstanbul Gedik Üniversitesi Bilgisayar Mühendisliği Bölümü **MFK420 Mezuniyet Projesi ve Tezi** kapsamında geliştirilen YAZTEK projesinin kaynak kodlarını ve dokümantasyonunu içermektedir.

## 📌 Proje Hakkında
YAZTEK, ilkokul 3. ve 4. sınıf öğrencilerinin okuma akıcılığını (hız, duraksama, hatalı okuma) yapay zekâ destekli ses tanıma (Speech-to-Text) teknolojileriyle otomatik olarak analiz eden bir eğitim platformudur. 

Sistem; geleneksel kronometre ve kâğıt-kalem ile yapılan okuma ölçümlerini dijitalleştirerek, öğretmenlere saniye bazında detaylı hata tespiti (WCPM - Dakikadaki Doğru Okunan Kelime metrikleri üzerinden) ve öğrenci gelişimini anlık takip edebilecekleri bir raporlama ekranı (Dashboard) sunar.

## 🚀 Temel Özellikler
* **Ses Tanıma ve Hata Tespiti:** Mikrofondan alınan ses kaydını metne dönüştürerek, referans metinle karşılaştırır ve atlanan/yanlış okunan kelimeleri tespit eder.
* **Hız ve Duraksama Analizi:** Kelimelerin okunma sürelerini ve aralardaki duraksamaları (timestamp) hesaplayarak net bir okuma hızı skoru çıkarır.
* **Mentör (Öğretmen) Kontrol Paneli:** Öğrenci bazlı detaylı raporlama, grafiksel gelişim takibi ve anlık veri görüntüleme sağlar.
* **Dinamik Metin Atama:** Seviyeye ve geçmiş hatalara göre öğrencilere yeni okuma metinleri önerme altyapısı sunar.

## 🛠️ Kullanılan Teknolojiler

**Frontend (İstemci Katmanı)**
* ReactJS

**Backend & Veritabanı (Uygulama ve Veri Katmanı)**
* .NET Core
* PostgreSQL

**Yapay Zekâ (YZ Servisi)**
* Türkçe ince ayar (fine-tune) yapılmış **Wav2Vec2 / XLSR**
* **BERTurk**
* **Llama-3**

**Altyapı**
* AWS Bulut Sunucuları ve Kubernetes

## 👥 Ekip ve Görev Dağılımı
* **Fatih Kenan Kaya:** Backend (.NET Core) servislerinin geliştirilmesi, yetkilendirme ve veritabanı (PostgreSQL) yönetimi.
* **Fatma Nur Atagün:** Yapay zekâ (ASR) modellerinin eğitilmesi, entegrasyonu ve süre/hata analiz algoritmalarının geliştirilmesi.
* **Doğan Fatih Oğur:** Kullanıcı arayüzlerinin (Frontend - React) geliştirilmesi, UI/UX tasarımı ve uçtan uca sistem testleri.

**Danışman:** Dr. Öğr. Üyesi Burcu Bektaş

---
*Not: Kurulum adımları ve yerel çalışma (localhost) talimatları geliştirme süreci ilerledikçe bu dosyaya eklenecektir.*
