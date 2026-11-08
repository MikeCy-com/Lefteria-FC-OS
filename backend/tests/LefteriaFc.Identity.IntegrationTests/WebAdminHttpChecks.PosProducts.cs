using System.Net.Http.Json;
using System.Text;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string PosProducts = "/api/admin/pos/products?organization_id=lefteria_fc";

    private static object ProductPayload(string category,string name="Fixture product",long? version=null,
        int price=150, string variant="small") =>
        version is null
            ? new {category_id=category,name,image_url=(string?)null,active=true,
                variants=new[]{new{id=variant,name="500ml",cost_cents=40,price_cents=price}}}
            : new {category_id=category,name,image_url=(string?)null,active=true,
                expected_version=version,
                variants=new[]{new{id=variant,name="500ml",cost_cents=40,price_cents=price}}};

    private static async Task<HttpResponseMessage> SaveProduct(
        HttpClient client,string cookie,HttpMethod verb,string path,object payload)
    {
        using var request=Mutation(path,JsonContent.Create(payload));
        request.Method=verb;
        request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task AdminPosProductWritesAsync()
    {
        using var anonymous=Client();
        using (var req=Mutation(PosProducts,JsonContent.Create(ProductPayload("fixture"))))
        using (var denial=await anonymous.SendAsync(req))
            await ExpectJsonAsync(denial,401,"not_authenticated");

        var fc=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,fc);
        var fixtureId=await database.SeedPosCatalogAsync();
        string? created=null;
        try
        {
            using (var response=await SaveProduct(client,login.Cookie,HttpMethod.Post,
                PosProducts,ProductPayload(fixtureId)))
            {
                var item=await ExpectJsonAsync(response,200);
                created=item.GetProperty("id").GetString();
                Require(created is not null && created.StartsWith("prod_",StringComparison.Ordinal)
                    && item.GetProperty("category_id").GetString()==fixtureId
                    && item.GetProperty("version").GetInt64()==0
                    && item.GetProperty("variants").GetArrayLength()==1
                    && item.GetProperty("variants")[0].GetProperty("price_cents").GetInt32()==150,
                    "Product create failed to return verified full variant set.");
            }
            await database.AssertProductAsync("lefteria_fc",created!,fixtureId,
                "Fixture product",true,0,1,150);
            Require(await database.ProductAuditCountAsync(fc)==1,
                "Product create did not create its atomic audit.");

            var route="/api/admin/pos/products/"+created+"?organization_id=lefteria_fc";
            using (var stale=await SaveProduct(client,login.Cookie,HttpMethod.Put,
                route,ProductPayload(fixtureId,"Stale",3)))
                await ExpectJsonAsync(stale,409,"pos_product_version_conflict");

            using (var updated=await SaveProduct(client,login.Cookie,HttpMethod.Put,
                route,ProductPayload(fixtureId,"Updated product",0,275,"large")))
            {
                var item=await ExpectJsonAsync(updated,200);
                Require(item.GetProperty("version").GetInt64()==1
                    && item.GetProperty("variants").GetArrayLength()==1
                    && item.GetProperty("variants")[0].GetProperty("id").GetString()=="large"
                    && item.GetProperty("variants")[0].GetProperty("price_cents").GetInt32()==275,
                    "Product update did not replace variants atomically.");
            }
            await database.AssertProductAsync("lefteria_fc",created!,fixtureId,
                "Updated product",true,1,1,275);
            Require(await database.ProductAuditCountAsync(fc)==2,
                "Product update did not append exactly one audit event.");

            using (var fcCatalog=await GetAsync(client,PosCatalogPath,login.Cookie))
            {
                var catalog=await ExpectJsonAsync(fcCatalog,200);
                Require(catalog.GetProperty("products").GetArrayLength()==2 &&
                    catalog.GetProperty("products").EnumerateArray().Any(p=>
                        p.GetProperty("id").GetString()==created &&
                        p.GetProperty("variants")[0].GetProperty("price_cents").GetInt32()==275),
                    "Read-only catalog did not observe committed product variant price.");
            }
            using (var academy=await GetAsync(client,AcademyPosCatalogPath,login.Cookie))
            {
                var catalog=await ExpectJsonAsync(academy,200);
                Require(catalog.GetProperty("products").GetArrayLength()==1 &&
                    !catalog.GetRawText().Contains("Updated product",StringComparison.Ordinal),
                    "Product change leaked across FC/Academy boundary.");
            }

            await database.WithPosProductAuditFailureAsync(fc, async()=>{
                using var denied=await SaveProduct(client,login.Cookie,HttpMethod.Put,
                    route,ProductPayload(fixtureId,"Audit rollback",1,444));
                await ExpectJsonAsync(denied,503);
            });
            await database.AssertProductAsync("lefteria_fc",created!,fixtureId,
                "Updated product",true,1,1,275);
            Require(await database.ProductAuditCountAsync(fc)==2,
                "Failed product audit left a partial variant, version or event.");

            foreach(var payload in new object[] {
                ProductPayload(fixtureId,"Zero price",null,0),
                ProductPayload(fixtureId,"Overflow price",null,10000001),
                new {category_id=fixtureId,name="Bad variant",active=true,
                    variants=new[]{
                        new{id="same",name="One",cost_cents=0,price_cents=100},
                        new{id="same",name="Two",cost_cents=0,price_cents=100}}},
                new {category_id=fixtureId,name="Missing variants",active=true},
                new {category_id=fixtureId,name="Unsafe image",image_url="javascript:alert(1)",
                    active=true,variants=new[]{new{id="small",name="Small",cost_cents=0,price_cents=100}}}
            })
                using(var denied=await SaveProduct(client,login.Cookie,HttpMethod.Post,PosProducts,payload))
                    await ExpectJsonAsync(denied,422,"invalid_product");

            using (var nested=Mutation(PosProducts,new StringContent(
                "{\"category_id\":\""+fixtureId+"\",\"name\":\"Nested\",\"active\":true,"+
                "\"variants\":[{\"id\":\"x\",\"id\":\"y\",\"name\":\"Bad\","+
                "\"cost_cents\":2,\"price_cents\":100}]}",Encoding.UTF8,"application/json")))
            {
                nested.Headers.Add("Cookie",login.Cookie);
                using var denied=await client.SendAsync(nested);
                await ExpectJsonAsync(denied,400,"invalid_json");
            }
            using (var unknown=await SaveProduct(client,login.Cookie,HttpMethod.Post,
                PosProducts,new {category_id=fixtureId,name="Unknown",active=true,
                    secret="illegal",variants=new[]{new{id="x",name="OK",cost_cents=0,price_cents=100}}}))
                await ExpectJsonAsync(unknown,400,"invalid_json");

            using (var foreign=await SaveProduct(client,login.Cookie,HttpMethod.Post,
                PosProducts+"",ProductPayload("wrong-owner-category")))
                await ExpectJsonAsync(foreign,422,"invalid_product");

            var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);
            using(var editorClient=Client())
            {
                var session=await LoginAsync(editorClient,editor);
                using var denied=await SaveProduct(editorClient,session.Cookie,HttpMethod.Put,
                    route,ProductPayload(fixtureId,"Editor denied",1));
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var academyOnly=await database.SeedAsync(organizations:["lefteria_fc_academy"]);
            using(var academyClient=Client())
            {
                var session=await LoginAsync(academyClient,academyOnly);
                using (var denied=await SaveProduct(academyClient,session.Cookie,HttpMethod.Put,
                    route,ProductPayload(fixtureId,"Cross owner",1)))
                    await ExpectJsonAsync(denied,403,"not_authorized");
                using (var permitted=await SaveProduct(academyClient,session.Cookie,HttpMethod.Put,
                    "/api/admin/pos/products/"+fixtureId+"?organization_id=lefteria_fc_academy",
                    ProductPayload(fixtureId,"Academy only",0,320)))
                    await ExpectJsonAsync(permitted,200);
            }
            await database.AssertProductAsync("lefteria_fc_academy",fixtureId,fixtureId,
                "Academy only",true,1,1,320);
            await database.AssertProductAsync("lefteria_fc",fixtureId,fixtureId,
                "FC fixture item",true,0,1,150);

            await database.RevokeOrganizationAsync(fc,"lefteria_fc");
            using (var denied=await SaveProduct(client,login.Cookie,HttpMethod.Put,
                route,ProductPayload(fixtureId,"Revoked",1)))
                await ExpectJsonAsync(denied,403,"not_authorized");
            await database.ExpireSessionAsync(fc,login.Token);
            using (var denied=await SaveProduct(client,login.Cookie,HttpMethod.Put,
                "/api/admin/pos/products/"+fixtureId+"?organization_id=lefteria_fc_academy",
                ProductPayload(fixtureId,"Expired",1)))
                await ExpectJsonAsync(denied,401,"not_authenticated");
            await database.AssertProductAsync("lefteria_fc",created!,fixtureId,
                "Updated product",true,1,1,275);
        }
        finally
        {
            if(created is not null) await database.CleanupCreatedProductAsync("lefteria_fc",created);
            await database.CleanupPosCatalogAsync(fixtureId);
        }
    }
}
