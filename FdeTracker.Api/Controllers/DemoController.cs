using FdeTracker.Api.Common;
using FdeTracker.Api.Filters;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>
/// Admin only: load or remove demo data. POST may carry the two training sheets (multipart fields "report" and "daily",
/// .xlsx, up to 1 MB each); their people, plan and status then fill the demo FDE program.
/// </summary>
[Route("api/admin/demo-data"), RequireApproved, RequireRole("admin")]
public sealed class DemoController(DemoService demo) : ApiControllerBase
{
    public const int MaxSheetBytes = 1_000_000;

    [HttpGet]
    public async Task<IActionResult> Status() => Ok(await demo.Status());

    [HttpPost, RequestFormLimits(MultipartBodyLengthLimit = 2_800_000)]
    public async Task<IActionResult> Load()
    {
        SheetData? sheet = null;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            async Task<MemoryStream?> Read(string name)
            {
                if (form.Files.GetFile(name) is not { } f) return null;
                if (f.Length > MaxSheetBytes) throw new ApiException(413, "Each sheet can be up to 1 MB");
                var ms = new MemoryStream();
                await f.CopyToAsync(ms);
                ms.Position = 0;
                if (ms.Length < 4 || ms.GetBuffer()[0] != (byte)'P' || ms.GetBuffer()[1] != (byte)'K') throw ApiException.BadRequest("Upload the sheets as .xlsx files");
                return ms;
            }
            await using var report = await Read("report");
            await using var daily = await Read("daily");
            if (report is not null || daily is not null)
            {
                try { sheet = DemoService.ReadSheets(report, daily); }
                catch (Exception e) when (e is not ApiException) { throw ApiException.BadRequest("Could not read the sheets. Upload the FDE status report and the 30-day status workbooks."); }
            }
        }
        return Ok(await demo.Load(sheet: sheet));
    }

    [HttpDelete]
    public async Task<IActionResult> Remove() => Ok(await demo.Remove());
}
