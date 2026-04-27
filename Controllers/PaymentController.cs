using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _service;

        public PaymentController(IPaymentService service)
        {
            _service = service;
        }

        // ✅ VNPay IPN: VNPay gọi server-to-server bằng GET với params trong query string
        [HttpGet("vnpay/ipn")]
        public async Task<IActionResult> VnPayIpn()
        {
            await _service.HandleVnPayIpnAsync(Request.Query, Request);
            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }

        // 🧪 TEMP - Trang kết quả thanh toán tạm thời khi chưa có frontend.
        // Đặt CallbackUrl = https://<ngrok>/api/payments/vnpay/callback để test.
        [HttpGet("vnpay/callback")]
        public IActionResult VnPayCallback()
        {
            var query = Request.Query;
            var responseCode = query["vnp_ResponseCode"].ToString();
            var txnRef       = query["vnp_TxnRef"].ToString();
            var amount       = query["vnp_Amount"].ToString();
            var orderInfo    = query["vnp_OrderInfo"].ToString();
            var bankCode     = query["vnp_BankCode"].ToString();
            var transactionNo = query["vnp_TransactionNo"].ToString();
            var payDate      = query["vnp_PayDate"].ToString();

            bool isSuccess = responseCode == "00";

            // Format amount: VNPay gửi amount * 100
            string formattedAmount = "";
            if (long.TryParse(amount, out long rawAmount))
                formattedAmount = (rawAmount / 100).ToString("N0") + " VNĐ";

            // Format pay date: yyyyMMddHHmmss → dd/MM/yyyy HH:mm:ss
            string formattedDate = payDate;
            if (payDate.Length == 14)
                formattedDate = payDate[6..8] + "/" + payDate[4..6] + "/" + payDate[0..4]
                              + " " + payDate[8..10] + ":" + payDate[10..12] + ":" + payDate[12..14];

            // Build all params table rows
            var allParamsRows = string.Join("", query
                .Where(q => q.Key != "vnp_SecureHash" && q.Key != "vnp_SecureHashType")
                .OrderBy(q => q.Key)
                .Select(q => "<tr><td class='pk'>" + q.Key + "</td><td class='pv'>" + q.Value + "</td></tr>"));

            string statusColor  = isSuccess ? "#4ade80" : "#f87171";
            string badgeBg      = isSuccess ? "rgba(74,222,128,.15)"      : "rgba(248,113,113,.15)";
            string badgeBorder  = isSuccess ? "rgba(74,222,128,.3)"       : "rgba(248,113,113,.3)";
            string icon         = isSuccess ? "✅" : "❌";
            string title        = isSuccess ? "Thanh toán thành công!"    : "Thanh toán thất bại";
            string subtitle     = isSuccess
                ? "Đơn đặt sân đã được xác nhận và xử lý."
                : "Giao dịch không thành công. Mã lỗi VNPay: <strong>" + responseCode + "</strong>";
            string statusLabel  = isSuccess ? "THÀNH CÔNG" : "THẤT BẠI";

            var css =
                "*{box-sizing:border-box;margin:0;padding:0}" +
                "body{font-family:'Inter',sans-serif;background:#0a0a1a;color:#e0e0ff;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px}" +
                ".card{background:#12122a;border:1px solid #2a2a4a;border-radius:16px;padding:40px;max-width:560px;width:100%;box-shadow:0 8px 48px rgba(0,0,200,.15)}" +
                ".icon{font-size:60px;text-align:center;margin-bottom:16px}" +
                "h1{text-align:center;font-size:22px;font-weight:700;margin-bottom:8px;color:" + statusColor + "}" +
                ".subtitle{text-align:center;color:#8b8bba;font-size:14px;margin-bottom:28px}" +
                ".summary{background:#0d0d22;border-radius:12px;padding:20px;margin-bottom:20px;display:grid;gap:14px}" +
                ".row{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}" +
                ".label{color:#8b8bba;font-size:13px;white-space:nowrap}" +
                ".value{font-weight:600;font-size:13px;text-align:right;word-break:break-all}" +
                ".amount{color:" + statusColor + ";font-size:20px;font-weight:700}" +
                ".badge{display:inline-block;padding:3px 12px;border-radius:20px;font-size:12px;font-weight:600;" +
                    "background:" + badgeBg + ";color:" + statusColor + ";border:1px solid " + badgeBorder + "}" +
                "details{margin-top:4px}" +
                "summary{cursor:pointer;color:#8b8bba;font-size:13px;padding:10px 0;user-select:none}" +
                "table{width:100%;border-collapse:collapse;margin-top:8px;background:#0d0d22;border-radius:8px;overflow:hidden}" +
                ".pk{padding:7px 12px;border-bottom:1px solid #1e1e3a;color:#8b8bba;font-size:12px;white-space:nowrap}" +
                ".pv{padding:7px 12px;border-bottom:1px solid #1e1e3a;color:#e0e0ff;font-size:12px;word-break:break-all;font-family:monospace}" +
                ".note{background:rgba(234,179,8,.08);border:1px solid rgba(234,179,8,.3);border-radius:8px;" +
                    "padding:12px 16px;margin-top:20px;font-size:13px;color:#fbbf24;line-height:1.6}" +
                "code{background:#0d0d22;padding:2px 6px;border-radius:4px;font-size:12px}";

            var html =
                "<!DOCTYPE html><html lang='vi'><head>" +
                "<meta charset='UTF-8'>" +
                "<meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
                "<title>Kết quả thanh toán – SmashCourt</title>" +
                "<link href='https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap' rel='stylesheet'>" +
                "<style>" + css + "</style>" +
                "</head><body><div class='card'>" +
                "<div class='icon'>" + icon + "</div>" +
                "<h1>" + title + "</h1>" +
                "<p class='subtitle'>" + subtitle + "</p>" +
                "<div class='summary'>" +
                    "<div class='row'><span class='label'>Trạng thái</span><span class='badge'>" + statusLabel + "</span></div>" +
                    "<div class='row'><span class='label'>Số tiền</span><span class='value amount'>" + formattedAmount + "</span></div>" +
                    "<div class='row'><span class='label'>Mã đơn</span><span class='value' style='font-size:11px'>" + txnRef + "</span></div>" +
                    "<div class='row'><span class='label'>Ngân hàng</span><span class='value'>" + bankCode + "</span></div>" +
                    "<div class='row'><span class='label'>Mã GD VNPay</span><span class='value'>" + transactionNo + "</span></div>" +
                    "<div class='row'><span class='label'>Nội dung</span><span class='value'>" + orderInfo + "</span></div>" +
                    "<div class='row'><span class='label'>Thời gian TT</span><span class='value'>" + formattedDate + "</span></div>" +
                "</div>" +
                "<details><summary>▶ Xem tất cả tham số VNPay (debug)</summary>" +
                "<table>" + allParamsRows + "</table></details>" +
                "<div class='note'>🧪 <strong>Trang test tạm thời</strong> — khi có frontend, đổi <code>CallbackUrl</code> trong <code>appsettings.Development.json</code> về URL frontend.</div>" +
                "</div></body></html>";

            return Content(html, "text/html; charset=utf-8");
        }
    }
}
