using System;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using System.Web.Security;
using CmsData;
using CmsWeb.Models;
using net.openstack.Core.Domain;
using net.openstack.Providers.Rackspace;
using UtilityExtensions;
using User = CmsData.User;

namespace CmsWeb.Areas.Manage.Controllers
{
    [RouteArea("Manage", AreaPrefix = "Account"), Route("{action}/{id?}")]
    public class AccountController : CmsControllerNoHttps
    {
        [HttpPost, MyRequireHttps]
        public ActionResult KeepAlive()
        {
            return Content("alive");
        }

        public ActionResult FroalaUpload(HttpPostedFileBase file)
        {
            var m = new AccountModel();
            string baseurl = null;

            var fn = $"{DbUtil.Db.Host}.{DateTime.Now:yyMMddHHmm}.{m.CleanFileName(Path.GetFileName(file.FileName))}";
            var error = string.Empty;
            var rackspacecdn = ConfigurationManager.AppSettings["RackspaceUrlCDN"];

            if (rackspacecdn.HasValue())
            {
                baseurl = rackspacecdn;
                var username = ConfigurationManager.AppSettings["RackspaceUser"];
                var key = ConfigurationManager.AppSettings["RackspaceKey"];
                var cloudIdentity = new CloudIdentity {APIKey = key, Username = username};
                var cloudFilesProvider = new CloudFilesProvider(cloudIdentity);
                cloudFilesProvider.CreateObject("AllFiles", file.InputStream, fn);
            }
            else // local server
            {
                baseurl = $"{Request.Url.Scheme}://{Request.Url.Authority}/Upload/";
                try
                {
                    var path = Server.MapPath("/Upload/");
                    path += fn;

                    path = m.GetNewFileName(path);
                    file.SaveAs(path);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    baseurl = string.Empty;
                }
            }
            return Json(new {link = baseurl + fn, error}, JsonRequestBehavior.AllowGet);
        }

        [HttpPost, MyRequireHttps]
        public ActionResult CKEditorUpload(string CKEditorFuncNum)
        {
            var m = new AccountModel();
            string baseurl = null;
            if (Request.Files.Count == 0)
                return Content("");
            var file = Request.Files[0];
            var fn = $"{DbUtil.Db.Host}.{DateTime.Now:yyMMddHHmm}.{m.CleanFileName(Path.GetFileName(file.FileName))}";
            var error = string.Empty;
            var rackspacecdn = ConfigurationManager.AppSettings["RackspaceUrlCDN"];

            if (rackspacecdn.HasValue())
            {
                baseurl = rackspacecdn;
                var username = ConfigurationManager.AppSettings["RackspaceUser"];
                var key = ConfigurationManager.AppSettings["RackspaceKey"];
                var cloudIdentity = new CloudIdentity {APIKey = key, Username = username};
                var cloudFilesProvider = new CloudFilesProvider(cloudIdentity);
                cloudFilesProvider.CreateObject("AllFiles", file.InputStream, fn);
            }
            else // local server
            {
                baseurl = $"{Request.Url.Scheme}://{Request.Url.Authority}/Upload/";
                try
                {
                    var path = Server.MapPath("/Upload/");
                    path += fn;

                    path = m.GetNewFileName(path);
                    file.SaveAs(path);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    baseurl = string.Empty;
                }
            }
            return Content($"<script type='text/javascript'>window.parent.CKEDITOR.tools.callFunction( {CKEditorFuncNum}, '{baseurl + fn}', '{error}' );</script>");
        }

        [Route("~/Abandon")]
        public ActionResult Abandon()
        {
            Session.Abandon();
            return Redirect("/");
        }

        [Route("~/ForceError")]
        public ActionResult ForceError()
        {
            var z = 0;
            var x = 2/z;
            return Content("error");
        }

        [Route("~/Error")]
        public ActionResult Error(string e)
        {
            ViewBag.Message = e;
            return View();
        }

        [Route("~/Logon")]
        [MyRequireHttps]
        public ActionResult LogOn(string returnUrl)
        {
            var r = DbUtil.CheckDatabaseExists(Util.CmsHost);
            var redirect = ViewExtensions2.DatabaseErrorUrl(r);
            if (redirect != null)
                return Redirect(redirect);

            var user = AccountModel.GetValidToken(Request.QueryString["otltoken"]);
            if (user.HasValue())
            {
                FormsAuthentication.SetAuthCookie(user, false);
                AccountModel.SetUserInfo(user, Session);
                if (returnUrl.HasValue() && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return Redirect("/");
            }

            var m = new AccountInfo {ReturnUrl = returnUrl};
            return View(m);
        }

        public static bool TryImpersonate()
        {
            if (System.Web.HttpContext.Current.User.Identity.IsAuthenticated)
                return false;
            if (!Util.IsDebug())
                return false;
#if Impersonate
#else
            if (!WebConfigurationManager.AppSettings["TryImpersonate"].ToBool())
                return false;
#endif
            var username = WebConfigurationManager.AppSettings["DebugUser"];
            if (!username.HasValue())
                return false;
            var session = System.Web.HttpContext.Current.Session;
            AccountModel.SetUserInfo(username, session);
            if (Util.UserId == 0)
                return false;
            FormsAuthentication.SetAuthCookie(username, false);
            return true;
        }

        [Route("~/Logon")]
        [HttpPost, MyRequireHttps]
        public ActionResult LogOn(AccountInfo m)
        {
            if (m.ReturnUrl.HasValue())
            {
                var lc = m.ReturnUrl.ToLower();
                if (lc.StartsWith("/default.aspx") || lc.StartsWith("/login.aspx"))
                    m.ReturnUrl = "/";
            }

            if (!m.UsernameOrEmail.HasValue())
                return View(m);

            var ret = AccountModel.AuthenticateLogon(m.UsernameOrEmail, m.Password, Session, Request);
            if (ret is string)
            {
                ViewBag.error = ret.ToString();
                return View(m);
            }
            var user = ret as User;

            if (user.MustChangePassword)
                return Redirect("/Account/ChangePassword");

            var access = DbUtil.Db.Setting("LimitAccess", "");
            if (access.HasValue())
                if (!user.InRole("Developer"))
                    return Message($"Site is {access}, contact {DbUtil.AdminMail} for help");

            var newleadertag = DbUtil.Db.FetchTag("NewOrgLeadersOnly", user.PeopleId, DbUtil.TagTypeId_System);
            if (newleadertag != null)
            {
                if(!user.InRole("Access")) // if they already have Access role, then don't limit them with OrgLeadersOnly
                    user.AddRoles(DbUtil.Db, "Access,OrgLeadersOnly".Split(','));
                DbUtil.Db.Tags.DeleteOnSubmit(newleadertag);
                DbUtil.Db.SubmitChanges();
            }

            if (!m.ReturnUrl.HasValue())
                if (!CMSRoleProvider.provider.IsUserInRole(user.Username, "Access"))
                    return Redirect("/Person2/" + Util.UserPeopleId);
            if (m.ReturnUrl.HasValue() && Url.IsLocalUrl(m.ReturnUrl))
                return Redirect(m.ReturnUrl);
            return Redirect("/");
        }

        [MyRequireHttps]
        public ActionResult LogOff()
        {
            DbUtil.Db.DeleteSpecialTags(Util.UserPeopleId);
            FormsAuthentication.SignOut();
            Session.Abandon();
            return Redirect("/");
        }

        [MyRequireHttps]
        public ActionResult ForgotUsername(string email)
        {
            if (Request.HttpMethod.ToUpper() == "GET")
                return View();

            if (!Util.ValidEmail(email))
                ModelState.AddModelError("email", "valid email required");
            if (!ModelState.IsValid)
                return View();

            email = email?.Trim();
            var q = from u in DbUtil.Db.Users
                    where u.Person.EmailAddress == email || u.Person.EmailAddress2 == email
                    where email != "" && email != null
                    select u;
            foreach (var user in q)
            {
                var message = DbUtil.Db.ContentHtml("ForgotUsername", Resource1.AccountController_ForgotUsername);
                message = message.Replace("{name}", user.Name);
                message = message.Replace("{username}", user.Username);
                DbUtil.Db.EmailRedacted(DbUtil.AdminMail, user.Person, "touchpoint forgot username", message);
                DbUtil.Db.SubmitChanges();
                DbUtil.Db.EmailRedacted(DbUtil.AdminMail,
                    CMSRoleProvider.provider.GetAdmins(),
                    $"touchpoint user: {user.Name} forgot username", "no content");
            }
            if (!q.Any())
                DbUtil.Db.EmailRedacted(DbUtil.AdminMail,
                    CMSRoleProvider.provider.GetAdmins(),
                    $"touchpoint unknown email: {email} forgot username", "no content");

            return RedirectToAction("RequestUsername");
        }

        [HttpGet, MyRequireHttps]
        public ActionResult ForgotPassword()
        {
            var m = new AccountInfo();
            return View(m);
        }

        [HttpPost, MyRequireHttps]
        public ActionResult ForgotPassword(AccountInfo m)
        {
            if (!ModelState.IsValid)
                return View(m);
            AccountModel.ForgotPassword(m.UsernameOrEmail);

            return RedirectToAction("RequestPassword");
        }

        [MyRequireHttps]
        public ActionResult CreateAccount(string id)
        {
            if (!id.HasValue())
                return Content("invalid URL");

            var pid = AccountModel.GetValidToken(id).ToInt();
            var p = DbUtil.Db.LoadPersonById(pid);
            if (p == null)
                return View("LinkUsed");
            var minage = DbUtil.Db.Setting("MinimumUserAge", "16").ToInt();
            if ((p.Age ?? 16) < minage)
                return Content($"must be Adult ({minage} or older)");
            var user = MembershipService.CreateUser(DbUtil.Db, pid);
            var newleadertag = DbUtil.Db.FetchTag("NewOrgLeadersOnly", p.PeopleId, DbUtil.TagTypeId_System);
            if (newleadertag != null)
            {
                if(!user.InRole("Access")) // if they already have Access role, then don't limit them with OrgLeadersOnly
                    user.AddRoles(DbUtil.Db, "Access,OrgLeadersOnly".Split(','));
                DbUtil.Db.Tags.DeleteOnSubmit(newleadertag);
                DbUtil.Db.SubmitChanges();
            }
            else // todo: remove this when things have settled
            {
                var roles = p.GetExtra("Roles");
                if(roles.HasValue())
                {
                    user.AddRoles(DbUtil.Db, roles.Split(','));
                    p.RemoveExtraValue(DbUtil.Db, "Roles");
                    DbUtil.Db.SubmitChanges();
                }
            }
            FormsAuthentication.SetAuthCookie(user.Username, false);
            AccountModel.SetUserInfo(user.Username, Session);

            ViewBag.user = user.Username;
            ViewBag.MinPasswordLength = MembershipService.MinPasswordLength;
            ViewBag.RequireSpecialCharacter = MembershipService.RequireSpecialCharacter;
            ViewBag.RequireOneNumber = MembershipService.RequireOneNumber;
            ViewBag.RequireOneUpper = MembershipService.RequireOneUpper;

            return View("SetPassword");
        }

        [MyRequireHttps]
        public ActionResult RequestPassword()
        {
            return View();
        }

        [MyRequireHttps]
        public ActionResult RequestUsername()
        {
            return View();
        }

        [MyRequireHttps]
        [Authorize]
        public ActionResult ChangePassword()
        {
            ViewBag.MinPasswordLength = MembershipService.MinPasswordLength;
            ViewBag.RequireSpecialCharacter = MembershipService.RequireSpecialCharacter;
            ViewBag.RequireOneNumber = MembershipService.RequireOneNumber;
            ViewBag.RequireOneUpper = MembershipService.RequireOneUpper;
            return View();
        }

        [MyRequireHttps]
        [HttpGet]
        public ActionResult SetPassword(Guid? id)
        {
            ViewBag.Id = id;
            return View("SetPasswordConfirm");
        }

        [MyRequireHttps]
        [HttpPost]
        public ActionResult SetPasswordConfirm(Guid? id)
        {
            if (!id.HasValue)
                return Content("invalid URL");
            var user = DbUtil.Db.Users.SingleOrDefault(u => u.ResetPasswordCode == id);
            if (user == null || (user.ResetPasswordExpires.HasValue && user.ResetPasswordExpires < DateTime.Now))
                return View("LinkUsed");
            user.ResetPasswordCode = null;
            user.IsLockedOut = false;
            user.FailedPasswordAttemptCount = 0;
            DbUtil.Db.SubmitChanges();
            FormsAuthentication.SetAuthCookie(user.Username, false);
            AccountModel.SetUserInfo(user.Username, Session);
            ViewBag.user = user.Username;
            ViewBag.MinPasswordLength = MembershipService.MinPasswordLength;
            ViewBag.RequireSpecialCharacter = MembershipService.RequireSpecialCharacter;
            ViewBag.RequireOneNumber = MembershipService.RequireOneNumber;
            ViewBag.RequireOneUpper = MembershipService.RequireOneUpper;
            return View("SetPassword");
        }

        [MyRequireHttps]
        [HttpPost]
        [Authorize]
        public ActionResult SetPassword(string newPassword, string confirmPassword)
        {
            ViewBag.user = User.Identity.Name;
            ViewBag.MinPasswordLength = MembershipService.MinPasswordLength;
            ViewBag.RequireSpecialCharacter = MembershipService.RequireSpecialCharacter;
            ViewBag.RequireOneNumber = MembershipService.RequireOneNumber;
            ViewBag.RequireOneUpper = MembershipService.RequireOneUpper;

            if (!ValidateChangePassword("na", newPassword, confirmPassword))
                return View();
            var mu = CMSMembershipProvider.provider.GetUser(User.Identity.Name, false);
            mu.UnlockUser();
            try
            {
                if (mu.ChangePassword(mu.ResetPassword(), newPassword))
                    return RedirectToAction("ChangePasswordSuccess");
                ModelState.AddModelError("form", "The current password is incorrect or the new password is invalid.");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("form", ex.Message);
            }
            return View();
        }

        [MyRequireHttps]
        [Authorize]
        [HttpPost]
        public ActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            ViewBag.user = User.Identity.Name;
            ViewBag.MinPasswordLength = MembershipService.MinPasswordLength;
            ViewBag.RequireSpecialCharacter = MembershipService.RequireSpecialCharacter;
            ViewBag.RequireOneNumber = MembershipService.RequireOneNumber;
            ViewBag.RequireOneUpper = MembershipService.RequireOneUpper;

            if (!ValidateChangePassword(currentPassword, newPassword, confirmPassword))
                return View();

            try
            {
                if (MembershipService.ChangePassword(User.Identity.Name, currentPassword, newPassword))
                    return RedirectToAction("ChangePasswordSuccess");
                ModelState.AddModelError("form", "The current password is incorrect or the new password is invalid.");
                return View();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("form", ex.Message);
                return View();
            }
        }

        [MyRequireHttps]
        public ActionResult ChangePasswordSuccess()
        {
            var rd = DbUtil.Db.Setting("RedirectAfterPasswordChange", "");
            if (rd.HasValue())
                return Redirect(rd);
            return View();
        }

        private bool ValidateChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            if (string.IsNullOrEmpty(currentPassword))
                ModelState.AddModelError("currentPassword", "You must specify a current password.");
            if (newPassword == null || newPassword.Length < MembershipService.MinPasswordLength)
                ModelState.AddModelError("newPassword",
                    string.Format(CultureInfo.CurrentCulture,
                        "You must specify a new password of {0} or more characters.",
                        MembershipService.MinPasswordLength));
            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
                ModelState.AddModelError("form", "The new password and confirmation password do not match.");
            return ModelState.IsValid;
        }

        public class AccountInfo
        {
            [Required]
            public string UsernameOrEmail { get; set; }

            public string Password { get; set; }
            public string ReturnUrl { get; set; }
        }
    }
}
