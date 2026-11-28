using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace LefteriaFc.Api.Marketing;

/// <summary>Render the validated per-child submission, not a reconstruction
/// from mutable player data. No external images, URLs or document templates.</summary>
public static class AcademyRegistrationPdf
{
    private static readonly SemaphoreSlim RenderSlots=new(2,2);
    private const float Width=595,Height=842,Left=38,Right=557;
    private static string Yes(bool value)=>value?"Ναι":"Όχι";

    public static byte[] Render(AcademyRegistrationRequest child,Guid registration,
        string policy,int childCount,int monthly,DateTimeOffset submitted)
    {
        if(!RenderSlots.Wait(0))throw new InvalidOperationException("Registration PDF renderer busy.");
        try
        {
            using var signature=DecodeSignature(child.SignatureData);
            using var stream=new MemoryStream();
            using var document=SKDocument.CreatePdf(stream)
                ?? throw new InvalidOperationException("Registration PDF unavailable.");
            using var regular=SKTypeface.FromFamilyName("DejaVu Sans")??SKTypeface.Default;
            using var bold=SKTypeface.FromFamilyName("DejaVu Sans",SKFontStyle.Bold)??regular;
            using var font=new SKFont(regular,11);
            using var heading=new SKFont(bold,12);
            using var titleFont=new SKFont(bold,17);
            using var ink=new SKPaint{Color=new SKColor(28,28,28),IsAntialias=true};
            using var gold=new SKPaint{Color=new SKColor(246,187,56),IsAntialias=true};
            using var dark=new SKPaint{Color=new SKColor(18,18,18),IsAntialias=true};
            using var white=new SKPaint{Color=SKColors.White,IsAntialias=true};
            SKCanvas? canvas=null;
            float y=0;
            int page=0;
            void NewPage()
            {
                if(canvas is not null)document.EndPage();
                canvas=document.BeginPage(Width,Height);
                page++;
                canvas.Clear(SKColors.White);
                canvas.DrawRect(0,0,Width,72,dark);
                canvas.DrawRect(0,72,Width,4,gold);
                canvas.DrawText("ΛΕΥΤΕΡΙΑ FC ACADEMY",Left,29,SKTextAlign.Left,titleFont,white);
                canvas.DrawText("ΑΙΤΗΣΗ ΕΓΓΡΑΦΗΣ · ΙΔΙΩΤΙΚΟ ΕΓΓΡΑΦΟ",Left,54,SKTextAlign.Left,heading,white);
                canvas.DrawText("Αίτηση "+registration.ToString("D")+" · "+submitted.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                    Left,94,SKTextAlign.Left,font,ink);
                canvas.DrawText("Σελίδα "+page,Left,815,SKTextAlign.Left,font,ink);
                y=121;
            }
            void Ensure(float space){if(canvas is null||y+space>778)NewPage();}
            void Section(string label)
            {
                Ensure(42);
                canvas!.DrawRect(Left,y-9,5,16,gold);
                canvas.DrawText(label,Left+12,y+3,SKTextAlign.Left,heading,ink);
                y+=25;
            }
            void Field(string label,string? value)
            {
                Ensure(43);
                canvas!.DrawText(label,Left,y,SKTextAlign.Left,heading,ink);
                y+=15;
                var text=string.IsNullOrWhiteSpace(value)?"—":value;
                // Break at rune boundaries, retain all submitted content.
                var line=new StringBuilder();
                foreach(var rune in text.EnumerateRunes())
                {
                    if(Rune.IsControl(rune))continue;
                    var candidate=line.ToString()+rune.ToString();
                    if(font.MeasureText(candidate)>Right-Left&&line.Length>0)
                    {
                        Ensure(16);
                        canvas!.DrawText(line.ToString(),Left,y,SKTextAlign.Left,font,ink);
                        y+=15;line.Clear();
                    }
                    line.Append(rune.ToString());
                }
                Ensure(16);
                canvas!.DrawText(line.ToString(),Left,y,SKTextAlign.Left,font,ink);
                y+=22;
            }
            NewPage();
            Section("1 · Στοιχεία αθλητή");
            Field("Όνομα",child.PlayerFirstName.Trim()+" "+child.PlayerLastName.Trim());
            Field("Ημερομηνία γέννησης",child.PlayerDob);
            Field("Φύλο",child.PlayerGender=="male"?"Αγόρι":"Κορίτσι");
            Field("Διεύθυνση",child.PlayerAddress+" · "+child.PlayerCity+" "+child.PlayerPostalCode);
            Field("Δελτίο υγείας ΚΟΑ",child.KoaHealthCardStatus=="yes"?"Ναι":"Όχι");
            Field("Προτιμώμενος αριθμός / εκτύπωση",child.PreferredNumber+" · "+child.PreferredPrintName);
            Section("2 · Κηδεμόνας & επικοινωνία");
            Field("Ονοματεπώνυμο / σχέση",child.ParentName+" · "+child.ParentRelationship);
            Field("Τηλέφωνο",child.ParentPhone);
            Field("Email",child.ParentEmail);
            Field("Επείγουσα επικοινωνία",child.EmergencyName+" · "+child.EmergencyRelationship+" · "+child.EmergencyPhone);
            Section("3 · Ιατρικές δηλώσεις κηδεμόνα");
            Field("Αλλεργίες",Yes(child.HasAllergies)+(child.HasAllergies?" · "+child.AllergiesDetails:""));
            Field("Παθήσεις",Yes(child.HasConditions)+(child.HasConditions?" · "+child.ConditionsDetails:""));
            Field("Φαρμακευτική αγωγή",Yes(child.HasMedication)+(child.HasMedication?" · "+child.MedicationDetails:""));
            Section("4 · Οικονομικές επιλογές");
            Field("Τρόπος πληρωμής",child.PaymentMethod switch{"cash"=>"Μετρητά","card"=>"Κάρτα","transfer"=>"Τραπεζική μεταφορά",_=>child.PaymentMethod});
            Field("Οικογενειακή συνδρομή",childCount+" παιδιά · €"+monthly+" / μήνα");
            Field("Εγγραφή / εξοπλισμός / εκτύπωση","€20 / €140 / €10 ανά παιδί");
            Section("5 · Συγκαταθέσεις & υπογραφή");
            Field("Έκδοση πολιτικής",policy);
            Field("Συμμετοχή",Yes(child.ConsentParticipation));
            Field("Ιατρική εξουσιοδότηση",Yes(child.ConsentMedicalAuth));
            Field("Προστασία προσωπικών δεδομένων",Yes(child.ConsentGdpr));
            Field("Φωτογραφίες / πολυμέσα",Yes(child.ConsentMedia=="yes"));
            Field("Επικοινωνίες",Yes(child.ConsentCommunications));
            Field("Viber / email / SMS",Yes(child.ConsentViber)+" / "+Yes(child.CommEmail)+" / "+Yes(child.CommSms));
            Field("Ευθύνη",Yes(child.ConsentLiability));
            Field("Οικονομικοί όροι",Yes(child.ConsentFinancial));
            Field("Ημερομηνία υπογραφής",child.SignatureDate);
            Ensure(140);
            canvas!.DrawText("Υπογραφή που υποβλήθηκε στη φόρμα",Left,y,SKTextAlign.Left,heading,ink);
            y+=13;
            var ratio=Math.Min(440f/signature.Width,90f/signature.Height);
            using var image=SKImage.FromBitmap(signature);
            canvas.DrawImage(image,new SKRect(Left,y,Left+signature.Width*ratio,y+signature.Height*ratio),
                new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.None));
            document.EndPage();
            document.Close();
            if(stream.Length<500||stream.Length>2_000_000)
                throw new InvalidOperationException("Registration PDF exceeds size bounds.");
            return stream.ToArray();
        }
        finally{RenderSlots.Release();}
    }

    private static SKBitmap DecodeSignature(string data)
    {
        const string prefix="data:image/png;base64,";
        if(!data.StartsWith(prefix,StringComparison.Ordinal)||data.Length>=20000)
            throw new InvalidDataException("Invalid registration signature.");
        byte[] bytes;
        try{bytes=Convert.FromBase64String(data[prefix.Length..]);}
        catch(FormatException){throw new InvalidDataException("Invalid registration signature.");}
        try
        {
            using var stream=new MemoryStream(bytes,writable:false);
            using var codec=SKCodec.Create(stream,out var created);
            if(codec is null||created!=SKCodecResult.Success||codec.EncodedFormat!=SKEncodedImageFormat.Png||
                codec.Info.Width<1||codec.Info.Height<1||codec.Info.Width>4096||codec.Info.Height>1600||
                (long)codec.Info.Width*codec.Info.Height>4_000_000)
                throw new InvalidDataException("Invalid registration signature.");
            using var srgb=SKColorSpace.CreateSrgb();
            var info=new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Premul,srgb);
            var bitmap=new SKBitmap();
            if(!bitmap.TryAllocPixels(info)||
                codec.GetPixels(info,bitmap.GetPixels(),bitmap.RowBytes,new SKCodecOptions(0))!=SKCodecResult.Success)
            {
                bitmap.Dispose();
                throw new InvalidDataException("Invalid registration signature.");
            }
            return bitmap;
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
}
