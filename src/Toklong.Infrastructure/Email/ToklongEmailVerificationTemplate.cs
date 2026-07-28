using System.Net;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Email;

public sealed class ToklongEmailVerificationTemplate(
    EmailVerificationOptions options)
    : IEmailVerificationTemplate
{
    private const string Subject =
        "รหัสยืนยันอีเมลใหม่ของคุณจาก TOKLONG";

    public RenderedEmail Render(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var displayCode = code.Length > 3
            ? $"{code[..3]} {code[3..]}"
            : code;
        var escapedCode = WebUtility.HtmlEncode(displayCode);
        var escapedLogoUrl =
            WebUtility.HtmlEncode(options.BrandLogoUrl);

        var textBody = $"""
            TOKLONG

            ยืนยันอีเมลใหม่ของคุณ

            กรอกรหัสนี้ในแอป TOKLONG เพื่อยืนยันการเปลี่ยนอีเมล

            {code}

            รหัสนี้ใช้ได้ภายใน 10 นาที

            หากคุณไม่ได้ขอเปลี่ยนอีเมล ไม่ต้องดำเนินการใด ๆ และห้ามบอกรหัสนี้กับผู้อื่น

            TOKLONG จะไม่ขอรหัสผ่าน เลขบัตร หรือข้อมูลบัญชีธนาคารผ่านอีเมลนี้
            """;

        var htmlBody = $$"""
            <!doctype html>
            <html lang="th">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{{Subject}}</title>
              <style>
                @media only screen and (max-width: 620px) {
                  .content-padding { padding:24px 20px !important; }
                  .body-copy { font-size:16px !important; }
                }
              </style>
            </head>
            <body style="margin:0;padding:0;background:#f3f7fc;color:#172033;font-family:Arial,'Noto Sans Thai',Tahoma,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;background:#f3f7fc;">
                <tr>
                  <td align="center" style="padding:24px 12px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px;background:#ffffff;border-radius:20px;">
                      <tr>
                        <td class="content-padding" style="padding:36px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;">
                            <tr>
                              <td align="center" style="padding:0 0 24px;">
                                <img src="{{escapedLogoUrl}}" alt="TOKLONG" width="96" height="32" style="display:block;width:96px;height:32px;border:0;">
                                <div style="margin-top:10px;color:#145fc7;font-size:18px;font-weight:700;letter-spacing:1.6px;">TOKLONG</div>
                              </td>
                            </tr>
                            <tr>
                              <td align="center" style="padding:0 0 12px;font-size:24px;line-height:1.4;font-weight:700;color:#172033;">
                                ยืนยันอีเมลใหม่ของคุณ
                              </td>
                            </tr>
                            <tr>
                              <td class="body-copy" align="center" style="padding:0 0 24px;font-size:16px;line-height:1.7;color:#475467;">
                                กรอกรหัสนี้ในแอป TOKLONG เพื่อยืนยันการเปลี่ยนอีเมล
                              </td>
                            </tr>
                            <tr>
                              <td align="center" style="padding:0 0 20px;">
                                <div style="display:inline-block;padding:16px 24px;border-radius:14px;background:#eaf4ff;color:#145fc7;font-size:32px;line-height:1.2;font-weight:700;letter-spacing:5px;">{{escapedCode}}</div>
                              </td>
                            </tr>
                            <tr>
                              <td class="body-copy" align="center" style="padding:0 0 28px;font-size:16px;line-height:1.7;color:#344054;">
                                รหัสนี้ใช้ได้ภายใน 10 นาที
                              </td>
                            </tr>
                            <tr>
                              <td class="body-copy" style="padding:18px 20px;border-radius:12px;background:#f8fafc;font-size:16px;line-height:1.7;color:#475467;">
                                หากคุณไม่ได้ขอเปลี่ยนอีเมล ไม่ต้องดำเนินการใด ๆ และห้ามบอกรหัสนี้กับผู้อื่น
                              </td>
                            </tr>
                            <tr>
                              <td style="padding:28px 0 0;font-size:14px;line-height:1.7;color:#667085;text-align:center;">
                                TOKLONG จะไม่ขอรหัสผ่าน เลขบัตร หรือข้อมูลบัญชีธนาคารผ่านอีเมลนี้
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new RenderedEmail(
            Subject,
            textBody,
            htmlBody);
    }
}
