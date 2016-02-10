using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Security;
using CmsData;
using CmsData.Codes;
using UtilityExtensions;

namespace CmsWeb.Areas.OnlineReg.Models
{
    public partial class OnlineRegModel
    {
        public void FinishLaterNotice()
        {
            var registerLink = EmailReplacements.CreateRegisterLink(masterorgid ?? Orgid,
                $"Resume registration for {Header}");
            var msg = "<p>Hi {first},</p>\n<p>Here is the link to continue your registration:</p>\n" + registerLink;
            Debug.Assert((masterorgid ?? Orgid) != null, "m.Orgid != null");
            var notifyids = DbUtil.Db.NotifyIds((masterorg ?? org).NotifyIds);
            var p = UserPeopleId.HasValue ? DbUtil.Db.LoadPersonById(UserPeopleId.Value) : List[0].person;
            DbUtil.Db.Email(notifyids[0].FromEmail, p, $"Continue your registration for {Header}", msg);
        }

        public string CheckDuplicateGift(decimal? amt)
        {
            if (!OnlineGiving() || !amt.HasValue)
                return null;

            var previousTransaction =
                (from t in DbUtil.Db.Transactions
                 where t.Amt == amt
                 where t.OrgId == Orgid
                 where t.TransactionDate > DateTime.Now.AddMinutes(-20)
                 where DbUtil.Db.Contributions.Any(cc => cc.PeopleId == List[0].PeopleId && cc.TranId == t.Id)
                 select t).FirstOrDefault();

            if (previousTransaction == null)
                return null;

            return @"
Thank you for your gift! Our records indicate that you recently submitted a gift in this amount a short while ago.
As a safeguard against duplicate transactions we recommend that you either wait 20 minutes,
or modify the amount of this gift by a small amount so that it does not appear as a duplicate. 
Thank you.
";
        }

        public RouteModel FinishRegistration(Transaction ti)
        {
            TranId = ti.Id;
            HistoryAdd("ProcessPayment");
            var ed = DbUtil.Db.RegistrationDatas.Single(dd => dd.Id == DatumId);
            ed.Data = Util.Serialize(this);
            ed.Completed = true;
            DbUtil.Db.SubmitChanges();

            try
            {
                LogOutOfOnlineReg();
                var view = ConfirmTransaction(ti.TransactionId);
                switch (view)
                {
                    case ConfirmEnum.Confirm:
                        return RouteModel.ViewAction("Confirm", this);
                    case ConfirmEnum.ConfirmAccount:
                        return RouteModel.ViewAction("ConfirmAccount", this);
                }
            }
            catch (Exception ex)
            {
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);
                return RouteModel.ErrorMessage(ex.Message);
            }
            return null;
        }

        public ConfirmEnum ConfirmTransaction(string TransactionReturn)
        {
            ParseSettings();
            if (List.Count == 0)
                throw new Exception(" unexpected, no registrants found in confirmation");

            CreateTransactionIfNeeded();
            SetConfirmationEmailAddress();

            if (CreatingAccount())
                return CreateAccount();

            if (OnlineGiving())
                return DoOnlineGiving(TransactionReturn);

            if (ManagingSubscriptions())
                return ConfirmManageSubscriptions();

            if (ChoosingSlots())
                return ConfirmPickSlots();

            if (OnlinePledge())
                return SendLinkForPledge();

            if (ManageGiving())
                return SendLinkToManageGiving();

            SetTransactionReturn(TransactionReturn);
            EnrollAndConfirm();
            UseCoupon(Transaction.TransactionId, Transaction.Amt ?? 0);
            return ConfirmEnum.Confirm;
        }

        private ConfirmEnum CreateAccount()
        {
            List[0].CreateAccount();
            return ConfirmEnum.ConfirmAccount;
        }

        private bool CreatingAccount()
        {
            return org != null && org.RegistrationTypeId == RegistrationTypeCode.CreateAccount;
        }

        private void SetTransactionReturn(string TransactionReturn)
        {
            if (!Transaction.TransactionId.HasValue())
            {
                Transaction.TransactionId = TransactionReturn;
                if (testing == true && !Transaction.TransactionId.Contains("(testing)"))
                {
                    Transaction.TransactionId += "(testing)";
                    Log("TestingTransaction");
                }
            }
        }

        private void SetConfirmationEmailAddress()
        {
            email = IsCreateAccount() || ManagingSubscriptions()
                ? List[0].person.EmailAddress
                : List[0].EmailAddress;
        }

        private ConfirmEnum DoOnlineGiving(string transactionReturn)
        {
            var p = List[0];
            if (p.IsNew)
                p.AddPerson(null, p.org.EntryPointId ?? 0);

            var staff = DbUtil.Db.StaffPeopleForOrg(p.org.OrganizationId)[0];
            var text = p.setting.Body;
            text = text.Replace("{church}", DbUtil.Db.Setting("NameOfChurch", "church"), ignoreCase: true);
            text = text.Replace("{amt}", (Transaction.Amt ?? 0).ToString("N2"));
            text = text.Replace("{date}", DateTime.Today.ToShortDateString());
            text = text.Replace("{tranid}", Transaction.Id.ToString());
            text = text.Replace("{account}", "");
            text = text.Replace("{email}", p.person.EmailAddress);
            text = text.Replace("{phone}", p.person.HomePhone.FmtFone());
            text = text.Replace("{contact}", staff.Name);
            text = text.Replace("{contactemail}", staff.EmailAddress);
            text = text.Replace("{contactphone}", p.org.PhoneNumber.FmtFone());
            var re = new Regex(@"(?<b>.*?)<!--ITEM\sROW\sSTART-->(?<row>.*?)\s*<!--ITEM\sROW\sEND-->(?<e>.*)",
                RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);
            var match = re.Match(text);
            var b = match.Groups["b"].Value;
            var row = match.Groups["row"].Value.Replace("{funditem}", "{0}").Replace("{itemamt}", "{1:N2}");
            var e = match.Groups["e"].Value;
            var sb = new StringBuilder(b);

            var desc = $"{p.person.Name}; {p.person.PrimaryAddress}; {p.person.PrimaryZip}";
            foreach (var g in p.FundItemsChosen())
            {
                if (g.amt > 0)
                {
                    sb.AppendFormat(row, g.desc, g.amt);
                    p.person.PostUnattendedContribution(DbUtil.Db, g.amt, g.fundid, desc, tranid: Transaction.Id);
                }
            }
            Transaction.TransactionPeople.Add(new TransactionPerson
            {
                PeopleId = p.person.PeopleId,
                Amt = Transaction.Amt,
                OrgId = Orgid,
            });
            Transaction.Financeonly = true;
            if (Transaction.Donate > 0)
            {
                var fundname = DbUtil.Db.ContributionFunds.Single(ff => ff.FundId == p.setting.DonationFundId).FundName;
                sb.AppendFormat(row, fundname, Transaction.Donate);
                Transaction.Fund = p.setting.DonationFund();
                p.person.PostUnattendedContribution(DbUtil.Db, Transaction.Donate ?? 0, p.setting.DonationFundId, desc,
                    tranid: Transaction.Id);
                Log("PostedContribution");
            }
            sb.Append(e);
            if (!Transaction.TransactionId.HasValue())
            {
                Transaction.TransactionId = transactionReturn;
                if (testing == true && !Transaction.TransactionId.Contains("(testing)"))
                    Transaction.TransactionId += "(testing)";
            }
            var contributionemail = (from ex in p.person.PeopleExtras
                                     where ex.Field == "ContributionEmail"
                                     select ex.Data).SingleOrDefault();
            if (contributionemail.HasValue())
                contributionemail = (contributionemail ?? "").Trim();

            if (Util.ValidEmail(contributionemail))
                Log("UsingSpecialEmail");
            else
                contributionemail = p.person.FromEmail;

            var body = sb.ToString();
            var from = Util.TryGetMailAddress(DbUtil.Db.StaffEmailForOrg(p.org.OrganizationId));

            DbUtil.Db.EmailFinanceInformation(from, p.person, p.setting.Subject, body);
            DbUtil.Db.EmailFinanceInformation(contributionemail, DbUtil.Db.StaffPeopleForOrg(p.org.OrganizationId),
                "online giving contribution received",
                $"see contribution records for {p.person.Name} ({p.PeopleId})");
            if (p.CreatingAccount)
                p.CreateAccount();
            return ConfirmEnum.Confirm;
        }

        private void CreateTransactionIfNeeded()
        {
            if (Transaction != null || ManagingSubscriptions() || ChoosingSlots())
                return;
            HistoryAdd("ConfirmTransaction");
            UpdateDatum(completed: true);
            var pf = PaymentForm.CreatePaymentForm(this);
            _transaction = pf.CreateTransaction(DbUtil.Db);
            TranId = _transaction.Id;
        }

        public static void ConfirmDuePaidTransaction(Transaction ti, string transactionId, bool sendmail)
        {
            var Db = DbUtil.Db;
            var org = Db.LoadOrganizationById(ti.OrgId);
            ti.TransactionId = transactionId;
            if (ti.Testing == true && !ti.TransactionId.Contains("(testing)"))
                ti.TransactionId += "(testing)";

            var amt = ti.Amt;
            var due = PaymentForm.AmountDueTrans(Db, ti);
            foreach (var pi in ti.OriginalTrans.TransactionPeople)
            {
                var p = Db.LoadPersonById(pi.PeopleId);
                if (p != null)
                {
                    var om = Db.OrganizationMembers.SingleOrDefault(m => m.OrganizationId == ti.OrgId && m.PeopleId == pi.PeopleId);
                    if (om == null)
                        continue;
                    Db.SubmitChanges();
                    if (org.IsMissionTrip == true)
                    {
                        Db.GoerSenderAmounts.InsertOnSubmit(
                            new GoerSenderAmount
                            {
                                Amount = ti.Amt,
                                GoerId = pi.PeopleId,
                                Created = DateTime.Now,
                                OrgId = org.OrganizationId,
                                SupporterId = pi.PeopleId,
                            });
                        var setting = Db.CreateRegistrationSettings(org.OrganizationId);
                        var fund = setting.DonationFundId;
                        p.PostUnattendedContribution(Db, ti.Amt ?? 0, fund,
                            $"SupportMissionTrip: org={org.OrganizationId}; goer={pi.PeopleId}", typecode: BundleTypeCode.Online);
                    }
                    var pay = amt;
                    if (org.IsMissionTrip == true)
                        ti.Amtdue = due;

                    var sb = new StringBuilder();
                    sb.AppendFormat("{0:g} ----------\n", Util.Now);
                    sb.AppendFormat("{0:c} ({1} id) transaction amount\n", ti.Amt, ti.Id);
                    sb.AppendFormat("{0:c} applied to this registrant\n", pay);
                    sb.AppendFormat("{0:c} total due all registrants\n", due);

                    om.AddToMemberDataBelowComments(sb.ToString());
                    var reg = p.SetRecReg();
                    reg.AddToComments(sb.ToString());
                    reg.AddToComments($"{org.OrganizationName} ({org.OrganizationId})");

                    amt -= pay;
                }
                else
                    Db.Email(Db.StaffEmailForOrg(org.OrganizationId),
                        Db.PeopleFromPidString(org.NotifyIds),
                        "missing person on payment due",
                        $"Cannot find {pi.Person.Name} ({pi.PeopleId}), payment due completed of {pi.Amt:c} but no record");
            }
            Db.SubmitChanges();
            var names = string.Join(", ", ti.OriginalTrans.TransactionPeople.Select(i => i.Person.Name).ToArray());

            var pid = ti.FirstTransactionPeopleId();
            var p0 = Db.LoadPersonById(pid);
            // question: should we be sending to all TransactionPeople?
            if (sendmail)
            {
                if (p0 == null)
                    Util.SendMsg(Util.SysFromEmail, Util.Host, Util.TryGetMailAddress(Db.StaffEmailForOrg(org.OrganizationId)),
                        "Payment confirmation", $"Thank you for paying {ti.Amt:c} for {ti.Description}.<br/>Your balance is {ti.Amtdue:c}<br/>{names}",
                        Util.ToMailAddressList(Util.FirstAddress(ti.Emails)), 0, pid);
                else
                {
                    Db.Email(Db.StaffEmailForOrg(org.OrganizationId), p0, Util.ToMailAddressList(ti.Emails),
                        "Payment confirmation", $"Thank you for paying {ti.Amt:c} for {ti.Description}.<br/>Your balance is {ti.Amtdue:c}<br/>{names}", false);
                    Db.Email(p0.FromEmail,
                        Db.PeopleFromPidString(org.NotifyIds),
                        "payment received for " + ti.Description,
                        $"{Transaction.FullName(ti)} paid {ti.Amt:c} for {ti.Description}, balance of {due:c}\n({names})");
                }
            }
        }
        public static void LogOutOfOnlineReg()
        {
            var session = HttpContext.Current.Session;
            if ((bool?)session["OnlineRegLogin"] == true)
            {
                FormsAuthentication.SignOut();
                session.Abandon();
            }
        }
    }
    public enum ConfirmEnum
    {
        Confirm,
        ConfirmAccount,
    }
}
