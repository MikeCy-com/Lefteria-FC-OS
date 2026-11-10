using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using LefteriaFc.Application.PublicContent;

namespace LefteriaFc.Api.Payabl;

public sealed record AcademyInvoiceItem(
    Guid Id, string OrganizationId, Guid PayerIdentityId,
    string PlayerId, string PlayerName,
    string GroupName, string PayerName, string PayerEmail, string PayerPhone,
    DateOnly FeeMonth, DateOnly DueDate, DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt, long AmountCents, string Currency, string Status,
    string PaymentMethod, string TransactionReference, string PaymentStatus,
    string? Description = null);

public sealed record AcademyInvoiceBatch(string OrganizationId,
    IReadOnlyList<AcademyInvoiceItem> Items);

/// <summary>
/// A4 invoice drawing port of original Lefteria-FC
/// backend/routes/charges.py::_build_receipt pinned at
/// 91eafcc9efd01ad177b4f13a869cd5af7041e5b2.
/// Same black/gold palette, physical mm coordinates and branded sections.
/// No current-time substitution for issue/payment dates, no card/PAN data.
/// </summary>
public static class AcademyInvoicePdf
{
    private const float Mm = 72f / 25.4f;
    private const float Width = 210f * Mm, Height = 297f * Mm;
    private static readonly SKColor Black = Color(18,18,18);
    private static readonly SKColor Gold = Color(245,166,35);
    private static readonly SKColor Ink = Color(26,26,26);
    private static readonly SKColor Muted = Color(115,115,115);
    private static readonly SKColor Grey = Color(240,240,240);
    private static readonly SKColor Alternate = Color(247,247,247);
    private static readonly SKColor Line = Color(217,217,217);
    private static readonly SKColor White = SKColors.White;

    public static AcademyInvoiceBatch Parse(string data)
    {
        var value=JsonSerializer.Deserialize<AcademyInvoiceBatch>(data,
            new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower });
        if(value?.OrganizationId!="lefteria_fc_academy" ||
            value.Items is null || value.Items.Count is < 1 or > 20)
            throw new InvalidOperationException("Invoice data is not verified.");
        var ids=new HashSet<Guid>();
        var owner=value.Items[0].PayerIdentityId;
        if(owner==Guid.Empty)throw new InvalidOperationException("Invoice payer missing.");
        foreach(var row in value.Items)
        {
            if(row.Id==Guid.Empty || !ids.Add(row.Id) ||
                row.PayerIdentityId!=owner || row.OrganizationId!=value.OrganizationId ||
                row.AmountCents is < 1 or > 100000000 || row.Currency!="EUR" ||
                row.FeeMonth.Day!=1 || row.DueDate.Year!=row.FeeMonth.Year ||
                row.DueDate.Month!=row.FeeMonth.Month ||
                row.PlayerId.Length is < 1 or > 128 ||
                row.PlayerName.Length is < 1 or > 200 ||
                row.PayerName.Length is < 1 or > 101 ||
                row.PayerEmail.Length is < 3 or > 50 ||
                row.PayerPhone.Length is < 9 or > 16 ||
                row.Status is not ("issued" or "paid" or "review_required") ||
                row.PaymentMethod is not ("cash" or "bank" or "card" or "online" or "other" or "unpaid") ||
                (row.Status=="paid") != row.PaidAt.HasValue ||
                (row.Status=="paid") != (row.PaymentMethod is "cash" or "bank" or "card" or "online" or "other") ||
                row.TransactionReference.Length>80)
                throw new InvalidOperationException("Invoice fields are inconsistent.");
        }
        return value;
    }

    public static byte[] Render(AcademyInvoiceBatch invoice, string? logoPath=null, PublicClub? club=null)
    {
        if(invoice.Items.Count is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(invoice));
        using var stream=new MemoryStream();
        using(var document=SKDocument.CreatePdf(stream))
        {
            if(document is null) throw new InvalidOperationException("PDF generator unavailable.");
            using var regular=SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
            using var bold=SKTypeface.FromFamilyName("DejaVu Sans",SKFontStyle.Bold) ?? regular;
            using var logo=LoadLogo(logoPath);
            var items=invoice.Items.OrderBy(i=>i.FeeMonth)
                .ThenBy(i=>i.PlayerName,StringComparer.Ordinal).ThenBy(i=>i.Id).ToArray();
            var count=items.Length;
            // The source uses one A4 page for 1-3 line items. For extra items
            // we paginate instead of drawing totals beneath the footer.
            var pages=new List<AcademyInvoiceItem[]>();
            for(int offset=0;offset<count;offset+=3)
                pages.Add(items.Skip(offset).Take(3).ToArray());
            for(var p=0;p<pages.Count;p++)
            {
                var canvas=document.BeginPage(Width,Height);
                DrawPage(canvas,pages[p],items,p,pages.Count,regular,bold,logo,club);
                document.EndPage();
            }
            document.Close();
        }
        if(stream.Length<500 || stream.Length>2_000_000)
            throw new InvalidOperationException("Generated invoice PDF is outside expected bounds.");
        return stream.ToArray();
    }

    private static SKBitmap? LoadLogo(string? path)
    {
        if(string.IsNullOrWhiteSpace(path) || !File.Exists(path))return null;
        try
        {
            var info=new FileInfo(path);
            if(info.Length>512_000)return null;
            var bitmap=SKBitmap.Decode(path);
            if(bitmap is null || bitmap.Width>2048 || bitmap.Height>2048 ||
                bitmap.Width<16 || bitmap.Height<16){bitmap?.Dispose();return null;}
            return bitmap;
        }
        catch{return null;}
    }
    private static SKColor Color(byte red,byte green,byte blue) => new(red,green,blue);
    private static float U(float mm)=>mm*Mm;
    private static string Date(DateOnly value)=>value.ToString("dd/MM/yyyy",CultureInfo.InvariantCulture);
    private static string Date(DateTimeOffset value)=>
        value.ToOffset(TimeSpan.FromHours(0)).ToString("dd/MM/yyyy",CultureInfo.InvariantCulture);
    private static string Amount(long cents)=>(cents/100m).ToString("F2",CultureInfo.InvariantCulture);
    private static string TextValue(string? value,int max=80)
    {
        if(string.IsNullOrWhiteSpace(value))return "—";
        var safe=new string(value.Normalize(NormalizationForm.FormD)
            .Where(x=>CharUnicodeInfo.GetUnicodeCategory(x)!=UnicodeCategory.NonSpacingMark
                && !char.IsControl(x)).ToArray()).Trim();
        return safe.Length>max?safe[..(max-1)]+"…":safe;
    }

    private static void Rect(SKCanvas c,float x,float y,float w,float h,SKColor fill,float r=0)
    {
        using var paint=new SKPaint{Color=fill,IsAntialias=true};
        if(r>0)c.DrawRoundRect(new SKRect(x,y,x+w,y+h),r,r,paint);
        else c.DrawRect(x,y,w,h,paint);
    }
    private static void Rule(SKCanvas c,float x,float y,float x2,float y2,SKColor color,float stroke=1)
    {
        using var paint=new SKPaint{Color=color,StrokeWidth=stroke,Style=SKPaintStyle.Stroke,IsAntialias=true};
        c.DrawLine(x,y,x2,y2,paint);
    }
    private static float Measure(string label,SKTypeface face,float size)
    {
        using var font=new SKFont(face,size);
        return font.MeasureText(label);
    }
    private static void Txt(SKCanvas c,string? label,float x,float y,
        SKTypeface face,float size,SKColor color,float? limit=null,
        bool center=false,bool right=false)
    {
        var safe=TextValue(label,170);
        using var font=new SKFont(face,size);
        if(limit is float max)
        {
            while(safe.Length>1 && font.MeasureText(safe)>max)
                safe=safe[..^2]+"…";
        }
        var width=font.MeasureText(safe);
        if(center)x-=width/2;else if(right)x-=width;
        using var paint=new SKPaint{Color=color,IsAntialias=true};
        // SkiaSharp 4: the five-argument DrawText overload is obsolete.
        // Use explicit alignment to keep .NET warnings-as-errors builds clean.
        c.DrawText(safe,x,y,SKTextAlign.Left,font,paint);
    }
    private static void Section(SKCanvas c,string heading,float left,float baseline,
        SKTypeface bold)
    {
        Rect(c,left,baseline-U(4),U(5),U(5),Gold);
        Txt(c,heading,left+U(8),baseline,bold,11,Ink);
    }

    private static void DrawPage(SKCanvas c,AcademyInvoiceItem[] pageItems,
        AcademyInvoiceItem[] full,int index,int pageCount,
        SKTypeface regular,SKTypeface bold,SKBitmap? logo,PublicClub? club)
    {
        var first=full[0];
        var total=full.Sum(v=>v.AmountCents);
        var settled=full.Where(x=>x.Status=="paid").Sum(x=>x.AmountCents);
        var owing=total-settled;
        var paid=owing==0;
        var mixed=settled>0 && owing>0;
        var pendingReview=full.Any(v=>v.Status=="review_required" ||
            v.PaymentStatus is "awaiting_settlement" or "review_required");
        var onlyOneChild=full.All(x=>x.PlayerId==first.PlayerId);
        var firstNo=first.Id.ToString("N")[..6].ToUpperInvariant();
        var invoiceNo=full.Length==1
            ? $"LFCA-{first.IssuedAt.UtcDateTime:yyyy}-{firstNo}"
            : $"LFCA-{first.IssuedAt.UtcDateTime:yyyy}-"+
              Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("|",full.Select(v=>v.Id.ToString("N"))))))[..6];
        // Black full-width 42mm header, two gold diagonal accents.
        Rect(c,0,0,Width,U(42),Black);
        using(var triangleBuilder=new SKPathBuilder())
        {
            triangleBuilder.MoveTo(Width-U(40),0);triangleBuilder.LineTo(Width,0);
            triangleBuilder.LineTo(Width,U(20));triangleBuilder.Close();
            using var triangle=triangleBuilder.Detach();
            using var paint=new SKPaint{Color=Gold,IsAntialias=true};
            c.DrawPath(triangle,paint);
        }
        using(var triangleBuilder=new SKPathBuilder())
        {
            triangleBuilder.MoveTo(Width-U(15),U(42));triangleBuilder.LineTo(Width,U(42));
            triangleBuilder.LineTo(Width,U(32));triangleBuilder.Close();
            using var triangle=triangleBuilder.Detach();
            using var paint=new SKPaint{Color=Gold,IsAntialias=true};
            c.DrawPath(triangle,paint);
        }
        Rect(c,0,0,3,U(42),Gold);
        using(var white=new SKPaint{Color=White,IsAntialias=true})
            c.DrawCircle(U(27),U(21),U(15),white);
        if(logo is not null)
            c.DrawBitmap(logo,new SKRect(U(13),U(7),U(41),U(35)),
                new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.None));

        var clubGreek=string.IsNullOrWhiteSpace(club?.GreekName)
            ? "ΛΕΥΤΕΡΙΑ FC" : club?.GreekName?.ToUpperInvariant() ?? "ΛΕΥΤΕΡΙΑ FC";
        Txt(c,clubGreek,U(50),U(18),bold,24,White,U(82));
        Txt(c,"ACADEMY",U(50),U(26),bold,18,Gold);
        Txt(c,"DISCIPLINE  •  DEVELOPMENT  •  DREAMS",U(50),U(32),
            regular,8,Color(191,191,191),U(77));

        // Original source _build_receipt: address, city/country, WhatsApp
        // phone, email and website are selected from the current club CMS.
        // Never manufacture club contact or accept a remote logo fetch.
        var contact=new List<(string Icon,string Label)>();
        if(!string.IsNullOrWhiteSpace(club?.Address))
            contact.Add(("*",club?.Address ?? ""));
        var city=string.Join(", ",new[]{club?.City,club?.Country}
            .Where(x=>!string.IsNullOrWhiteSpace(x)));
        if(city.Length>0)contact.Add(("",city));
        if(!string.IsNullOrWhiteSpace(club?.Phone))
            contact.Add(("T",(club?.Phone ?? "")+" (WhatsApp)"));
        if(!string.IsNullOrWhiteSpace(club?.Email))
            contact.Add(("@",club?.Email ?? ""));
        if(!string.IsNullOrWhiteSpace(club?.Website))
            contact.Add(("w",(club?.Website ?? "").Replace("https://","")
                .Replace("http://","")));
        for(var i=0;i<Math.Min(4,contact.Count);i++)
        {
            var y=U(15+i*5);
            Txt(c,contact[i].Icon,U(130),y,bold,8.5f,Gold);
            Txt(c,contact[i].Label,U(136),y,regular,8.5f,White,U(60));
        }

        Txt(c,"INVOICE",U(20),U(60),bold,32,Ink);
        Rule(c,U(20),U(63),U(65),U(63),Gold,3);
        Rect(c,U(110),U(52),Width-U(130),U(30),Grey,U(2));
        var metadata=new[]{
            ("Invoice No:",invoiceNo),("Issue Date:",Date(first.IssuedAt)),
            ("Due Date:",Date(full.Max(x=>x.DueDate)))};
        for(int i=0;i<metadata.Length;i++)
        {
            var y=U(59+8*i);
            Txt(c,metadata[i].Item1,U(116),y,regular,8.5f,Muted);
            Txt(c,metadata[i].Item2,U(144),y,bold,10,Ink,U(43));
        }

        Section(c,"PARENT / GUARDIAN DETAILS",U(20),U(92),bold);
        Section(c,"PLAYER DETAILS",U(109),U(92),bold);
        void Field(float x,float y,string name,string? value,int limit)
        {
            Txt(c,name,x,U(y),bold,9,Ink);
            Txt(c,value,x+U(25),U(y),regular,10,Ink,U(limit));
        }
        Field(U(20),102,"Name:",first.PayerName,56);
        Field(U(20),108,"Phone:",first.PayerPhone,56);
        Field(U(20),114,"Email:",first.PayerEmail,56);
        Field(U(109),102,"Player:",onlyOneChild?first.PlayerName:$"Family ({full.Select(v=>v.PlayerId).Distinct().Count()} players)",55);
        Field(U(109),108,"DOB:","—",55);
        Field(U(109),114,"Age Group:",onlyOneChild?first.GroupName:"Academy",55);
        Field(U(109),120,"Team:","Academy",55);
        Rule(c,U(105),U(97),U(105),U(124),Line,1);

        const float header=134, headerHeight=9, rowHeight=10;
        var tableX=U(20);var tableW=Width-U(40);
        Rect(c,tableX,U(header),tableW,U(headerHeight),Black);
        Txt(c,"Description",tableX+U(6),U(140),bold,10,White);
        Txt(c,"Qty",tableX+U(110),U(140),bold,10,White,center:true);
        Txt(c,"Unit Price (€)",tableX+U(135),U(140),bold,10,White,center:true);
        Txt(c,"Total (€)",tableX+U(160),U(140),bold,10,White,center:true);
        for(int j=0;j<pageItems.Length;j++)
        {
            var item=pageItems[j];var rowTop=header+headerHeight+j*rowHeight;
            if(j%2==0)Rect(c,tableX,U(rowTop),tableW,U(rowHeight),Alternate);
            var description=string.IsNullOrWhiteSpace(item.Description)
                ? $"Training {item.FeeMonth:yyyy-MM}" : item.Description;
            var desc=full.Length>1
                ? $"{item.PlayerName} · {description}" : description;
            Txt(c,desc,tableX+U(6),U(rowTop+6.5f),regular,10,Ink,U(95));
            Txt(c,"1",tableX+U(110),U(rowTop+6.5f),regular,10,Ink,center:true);
            Txt(c,Amount(item.AmountCents),tableX+U(135),U(rowTop+6.5f),
                regular,10,Ink,center:true);
            Txt(c,Amount(item.AmountCents),tableX+U(160),U(rowTop+6.5f),
                bold,10,Ink,center:true);
        }
        var lastRow=header+headerHeight+pageItems.Length*rowHeight;
        var totalTop=lastRow+2;
        Rect(c,tableX,U(totalTop),tableW*.55f,U(11),Alternate);
        Rect(c,tableX+tableW*.55f,U(totalTop),tableW*.45f,U(11),Gold);
        Txt(c,"Total Amount (€)",tableX+tableW*.68f,U(totalTop+7),bold,12,Ink,right:true);
        Txt(c,Amount(total),tableX+tableW-U(6),U(totalTop+7.5f),bold,16,Ink,right:true);

        var heading=totalTop+29;
        Section(c,"PAYMENT METHOD",U(20),U(heading),bold);
        var paymentTop=heading+7;
        Rect(c,U(20),U(paymentTop),Width-U(40),U(22),Grey,U(2));
        // Gold card icon and stripe, matching original PDF geometry.
        Rect(c,U(26),U(paymentTop+6),U(12),U(8),Gold,U(1));
        Rect(c,U(26),U(paymentTop+8.5f),U(12),U(1.5f),Black);
        var differentMethods=full.Where(x=>x.Status=="paid")
            .Select(x=>x.PaymentMethod).Distinct().ToArray();
        // The original ReportLab PDF supports cash, bank, card, online and
        // other. Only the verified finance ledger can supply a settled method:
        // parent/browser input can never turn an unpaid invoice into paid.
        var sourcePaymentLabels=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["cash"]="Cash",["bank"]="Bank Transfer",
            ["card"]="Paid by Card",["online"]="Paid Online",["other"]="Other"
        };
        var method=paid
            ? differentMethods.Length==1
                ? sourcePaymentLabels[differentMethods[0]] : "Multiple Payment Methods"
            : pendingReview ? "Payment Under Review"
                : mixed ? "Partially Paid" : "Awaiting Payment";
        Txt(c,method,U(45),U(paymentTop+7),bold,11,Ink);
        if(paid)
        {
            var refText=full.Length==1?first.TransactionReference:
                invoiceNo;
            Txt(c,"Transaction Ref: "+TextValue(refText,30).ToUpperInvariant(),
                U(45),U(paymentTop+12),regular,9,Muted,U(60));
            Txt(c,"Paid on: "+Date(full.Max(v=>v.PaidAt!.Value)),
                U(45),U(paymentTop+16.5f),regular,9,Muted);
            Txt(c,"Thank you for your payment!",Width/2+U(8),U(paymentTop+11),
                bold,11,Ink,U(75));
        }
        else
        {
            Txt(c,"Due Date: "+Date(full.Max(x=>x.DueDate)),U(45),U(paymentTop+12),
                regular,9,Muted);
            Txt(c,"Amount Due: € "+Amount(owing),U(45),U(paymentTop+16.5f),
                regular,9,Muted);
            Txt(c,pendingReview?"Awaiting payment verification.":
                mixed?"Remaining balance is due.":"Please settle by the due date.",
                Width/2+U(8),U(paymentTop+11),bold,11,Color(219,89,28),U(75));
        }

        var thankTop=paymentTop+34;
        Txt(c,"Thank you for being part of the Lefteria FC Academy family!",
            U(20),U(thankTop),bold,11,Ink,U(170));
        Txt(c,"Together we build a brighter future for our players.",
            U(20),U(thankTop+6),regular,10,Muted);
        Rule(c,U(20),U(thankTop+10),U(100),U(thankTop+10),Gold,1.5f);
        Txt(c,"Lefteria FC Academy",U(20),U(thankTop+16),bold,11,Ink);
        Txt(c,"One Team   One Dream",U(20),U(thankTop+21),regular,9,Muted);
        if(pageCount>1)
            Txt(c,$"Page {index+1} of {pageCount}",Width-U(20),U(263),
                regular,9,Muted,right:true);
        Footer(c,regular,bold,logo);
    }

    private static void Footer(SKCanvas c,SKTypeface regular,SKTypeface bold,SKBitmap? logo)
    {
        Rect(c,0,Height-U(22),Width,U(22),Black);
        Rect(c,0,Height-U(22),Width,1,Gold);
        Txt(c,"◉  ◎  ▲",U(15),Height-U(11),regular,12,Gold);
        Txt(c,"@lefteriafcacademy",U(35),Height-U(11),regular,9,White);
        Txt(c,"ΛΕΥΤΕΡΙΑ FC ACADEMY",Width-U(15),Height-U(11),
            bold,10,Gold,right:true);
        if(logo is not null)
            c.DrawBitmap(logo,new SKRect(Width/2-U(8),Height-U(19),
                Width/2+U(8),Height-U(3)),
                new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.None));
    }
}
