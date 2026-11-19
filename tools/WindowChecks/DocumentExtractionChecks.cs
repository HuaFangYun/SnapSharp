using SnapSharp.Models;
using SnapSharp.Services;
using System.Windows;

namespace SnapSharp.WindowChecks;

internal static partial class Program
{
    private static void PublicSampleExtractionChecks()
    {
        RecognizedBlock B(string text, double x, double y, double w = 200, double h = 20, string kind = "text") => new(text, new Rect(x, y, w, h), .98, kind);
        IdentityDocumentResult Read(params RecognizedBlock[] blocks) => IdentityDocumentRecognition.Recognize(new RecognitionDocument(blocks, "public sample regression"));
        string V(IdentityDocumentResult r, string n) => r.Fields.Single(f => f.Name == n).Value;

        var bank = Read(B("招商银行", 199, 256), B("银联", 578, 511), B("6225", 179, 422, 94, 32), B("7688", 298, 424, 94, 28),
            B("8888", 410, 424, 96, 28), B("8888", 528, 422, 94, 31), B("07/23", 273, 475),
            B("6225168888888888", 153, 422, 495, 32, "document-retry"));
        Require(bank.Kind == IdentityDocumentKind.BankCard && V(bank, "卡号").Length == 16 && V(bank, "发卡行") == "招商银行" && V(bank, "有效期") == "07/23",
            "Bank cards without a type label, unlabeled issuer/expiry and alternate number evidence must be supported.");
        Require(bank.Fields.Single(f => f.Name == "卡号").NeedsReview, "Conflicting bank numbers must be flagged, not silently merged.");
        var checksumConflict = Read(B("招商银行 银联", 20, 20),
            new RecognizedBlock("4111111111111111", new Rect(20, 60, 300, 20), .90),
            new RecognizedBlock("4111111111111112", new Rect(20, 60, 300, 20), .99, "document-retry"));
        Require(V(checksumConflict, "卡号") == "4111111111111112" && checksumConflict.Fields.Single(f => f.Name == "卡号").NeedsReview,
            "Checksum validity must flag ambiguity, not replace stronger actual OCR evidence with another digit sequence.");
        var partialBank = Read(B("招商银行 一卡通 银联", 20, 20), B("6226 0902 1014", 20, 60));
        Require(V(partialBank, "卡号") == "622609021014" && partialBank.Fields.Single(f => f.Name == "卡号").NeedsReview,
            "An incomplete number should retain the recognized digits and require review, never be padded to a full number.");

        var passport = Read(B("PASSPORT", 50, 20), B("姓/Surname", 344, 407, 86), B("张/ZHANG", 351, 436, 148),
            B("名/Given name", 346, 466, 105), B("三/SAN", 349, 493, 115), B("护照号/PassportNo.", 694, 333, 169), B("G12345678", 696, 358, 172),
            B("出生日期/Dateofbirh", 342, 581, 154), B("22SEP1992", 349, 609, 179),
            B("签发地点/Vaceofissue", 588, 582, 153), B("上海/SHANGHAI", 591, 608, 188),
            B("有效期至/Dateofexplry", 589, 638, 155), B("19AUG2020", 592, 665, 176));
        Require(passport.Kind == IdentityDocumentKind.Passport && V(passport, "姓名") == "张三" && V(passport, "护照号") == "G12345678",
            "Passport English translations must not become field values, and surname/given name must remain distinct.");
        Require(V(passport, "出生日期") == "22SEP1992" && V(passport, "签发地") == "上海/SHANGHAI" && V(passport, "有效期") == "19AUG2020",
            "Passport below-label extraction crossed columns.");

        var household = Read(B("常住人口登记卡", 792, 235, 602, 59), B("姓", 284, 347, 58, 58), B("名", 540, 347, 57, 58),
            B("李晨曦", 651, 350, 146, 50), B("户主或与", 1198, 332, 258, 41), B("子", 1504, 344, 64, 65),
            B("曾用名 李新星", 292, 453, 500, 46), B("性别 男", 1184, 446, 380, 54), B("民族 汉", 1185, 548, 380, 58),
            B("出生日期 1999年09月19日", 1199, 659, 640, 39), B("本市(县)其他住址 宗教信仰", 298, 762, 1250, 40),
            B("本市(县)其他住址」「宗教信仰」", 265, 762, 1319, 41, "document-retry"), B("310110199909192329", 660, 864, 420, 33),
            B("文化程度 小学 婚姻状况 未婚 兵役状况", 295, 963, 1250, 41));
        Require(household.Kind == IdentityDocumentKind.HouseholdRegister && V(household, "姓名") == "李晨曦" && V(household, "与户主关系") == "子",
            "Household title must disambiguate it from ID cards; former names and neighboring cells must not overwrite the name.");
        Require(V(household, "文化程度") == "小学" && V(household, "地址") == "" && V(household, "身份证号") == "310110199909192329",
            "Household table cells consumed an adjacent label or OCR grid punctuation.");

        var driving = Read(B("中华人民共和国机动车驾驶证", 242, 238, 329), B("姓名", 165, 310, 37, 23), B("刘洋", 221, 317, 90, 24),
            B("别女中国", 383, 305, 227, 47), B("国籍", 480, 313, 38, 19), B("准驾车型 Class C1", 282, 492, 230));
        Require(V(driving, "姓名") == "刘洋" && V(driving, "性别") == "女" && V(driving, "国籍") == "中国" && V(driving, "准驾车型") == "C1",
            "Driving name/gender/nationality and English class heading must not be concatenated.");
        var id = Read(B("李明", 151, 231, 143, 32), B("性别男民族汉", 150, 284, 217, 27), B("出生1985年4月10日", 153, 332, 266, 23),
            B("公民身份号码440305198504102577", 154, 506, 477, 23));
        Require(V(id, "姓名") == "李明" && id.Fields.Single(f => f.Name == "姓名").NeedsReview, "An isolated name whose label was missed should retain spatial evidence and a review notice.");

        var tightScope = Read(B("营业执照", 286, 207, 228, 44), B("经营范围", 250, 473, 93, 16),
            B("计算机软件（不含专", 349, 475, 201, 16), B("营、专控、专卖商品）；", 354, 488, 196, 16), B("国内商业。", 354, 501, 192, 16));
        Require(V(tightScope, "经营范围") == "计算机软件(不含专营、专控、专卖商品);国内商业。", "Overlapping expanded detection boxes must not drop a tight continuation line.");
        Console.WriteLine("PASS: bank, passport columns, household grid, bilingual driving fields, missing ID name label and tightly spaced business scope regressions from public samples.");
    }
}
