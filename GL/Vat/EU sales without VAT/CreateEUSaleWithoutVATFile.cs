using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#if !SILVERLIGHT
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
#else
using System.Windows;
#endif
using Uniconta.API.System;
using Uniconta.ClientTools;
using Uniconta.ClientTools.Controls;
using Uniconta.ClientTools.DataModel;
using Uniconta.ClientTools.Util;
using Uniconta.Common;
using Uniconta.Common.Enums;
using Uniconta.Common.Utility;
using Uniconta.DataModel;
using UnicontaClient.Creditor.Payments;

using UnicontaClient.Pages;
namespace UnicontaClient.Pages.CustomPage
{
    class CreateEUSaleWithoutVATFile
    {
#if !SILVERLIGHT

        #region Constants
        public const string VALIDATE_OK = "Ok";
        #endregion

        #region Variables
        private CrudAPI api;
        private string companyRegNo;
        private CountryCode companyCountryId;
        #endregion

        public CreateEUSaleWithoutVATFile(CrudAPI api, string companyRegNo, CountryCode companyCountryId)
        {
            this.api = api;
            this.companyRegNo = companyRegNo;
            this.companyCountryId = companyCountryId;
        }

        public bool CreateFile(IEnumerable<EUSaleWithoutVAT> listOfEUSaleWithoutVAT)
        {
            var result = listOfEUSaleWithoutVAT.Where(s => s.SystemInfo == VALIDATE_OK).ToList();
            if (result.Count > 0)
            {
                // Estonian XML generating
                if (api.CompanyEntity._CountryId == CountryCode.Estonia)
                {
                    GenerateXmlReport(result, api);
                    return true;
                }

                var sfd = UtilDisplay.LoadSaveFileDialog;
                sfd.Filter = UtilFunctions.GetFilteredExtensions(FileextensionsTypes.CSV);
                var userClickedSave = sfd.ShowDialog();
                if (userClickedSave != true)
                    return false;

                try
                {
                    using (var stream = File.Create(sfd.FileName))
                    {
                        var sw = new StreamWriter(stream, Encoding.Default);
                        CreateAndStreamFirstAndLast(result, sw, true, companyRegNo);
                        var sumOfAmount = StreamToFile(result, sw);
                        CreateAndStreamFirstAndLast(result, sw, false, companyRegNo, result.Count, sumOfAmount);
                        stream.Flush();
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    UnicontaMessageBox.Show(ex, Uniconta.ClientTools.Localization.lookup("Exception"));
                    return false;
                }
            }
            return true;
        }

        public class EUSaleWithoutVATSort : IEqualityComparer<EUSaleWithoutVAT> 
        {
            public bool Equals(EUSaleWithoutVAT x, EUSaleWithoutVAT y)
            {
                return (x.Country == y.Country) && (x.IsTriangularTrade == y.IsTriangularTrade) && (x._DebtorRegNoFile == y._DebtorRegNoFile);
            }
            public int GetHashCode(EUSaleWithoutVAT obj)
            {
                return ((int)obj.Country + 1) * (obj.IsTriangularTrade ? 2 : 1) * (obj._DebtorRegNoFile != null ? obj._DebtorRegNoFile.GetHashCode() : 1);
            }
        }

        public List<EUSaleWithoutVAT> CompressEUsale(IEnumerable<EUSaleWithoutVAT> listOfEU)
        {
            var dictEUSale = new Dictionary<EUSaleWithoutVAT, EUSaleWithoutVAT>(new EUSaleWithoutVATSort());
            
            foreach (var euSale in listOfEU)
            {
                euSale.Compressed = true;
                if (euSale.IsTriangularTrade)
                {
                    euSale.TriangularTradeAmount += euSale.ItemAmount;
                    euSale.ItemAmount = 0;
                    euSale.ServiceAmount = 0;
                }

                EUSaleWithoutVAT found;
                if (dictEUSale.TryGetValue(euSale, out found))
                {
                    found.ItemAmount += euSale.ItemAmount;
                    found.TriangularTradeAmount += euSale.TriangularTradeAmount;
                    found.ServiceAmount += euSale.ServiceAmount;
                    found.InvoiceNumber = 0;
                    found.Item = null;
                    found.Vat = null;
                }
                else
                    dictEUSale.Add(euSale, euSale);
            }

            if (dictEUSale.Count == 0)
                return null;

            return dictEUSale.Values.ToList();
        }

        public bool PreValidate()
        {
            if (!Country2Language.IsEU(companyCountryId))
            {
                UnicontaMessageBox.Show(Localization.lookup("AccountCountryNotEu"), Localization.lookup("Warning"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(companyRegNo))
            {
                UnicontaMessageBox.Show(string.Format(Localization.lookup("MissingOBJ"), Localization.lookup("CompanyRegNo")), Localization.lookup("Warning"));
                return false;
            }
        
            return true;
        }

        public void Validate(IEnumerable<EUSaleWithoutVAT> listOfEU, bool compressed, bool onlyValidate)
        {
            var countErr = 0;
            foreach (var euSale in listOfEU)
            {
                var hasErrors = false;
                var debtor = euSale.DebtorRef;
                euSale.SystemInfo = VALIDATE_OK;

                if (compressed && euSale.Compressed == false)
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = Localization.lookup("CompressPosting");
                    else
                        euSale.SystemInfo += Environment.NewLine + Localization.lookup("CompressPosting");
                }

                if (euSale.ItemAmount == 0 && euSale.ServiceAmount == 0 && euSale.TriangularTradeAmount == 0)
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = Localization.lookup("NoValues");
                    else
                        euSale.SystemInfo += Environment.NewLine + Localization.lookup("NoValues");
                }
                else if (euSale.IsTriangularTrade && euSale.TriangularTradeAmount == 0)
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = "Triangular trade amount is 0";
                    else
                        euSale.SystemInfo += Environment.NewLine + "Triangular trade amount is 0";
                }

                if (debtor.Country == CountryCode.Unknown)
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = Localization.lookup("CountryNotSet");
                    else
                        euSale.SystemInfo += Environment.NewLine + Localization.lookup("CountryNotSet");
                }
                else if (debtor.Country == companyCountryId)
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = Localization.lookup("OwnCountryProblem");
                    else
                        euSale.SystemInfo += Environment.NewLine + Localization.lookup("OwnCountryProblem");
                }

                if (string.IsNullOrWhiteSpace(euSale._DebtorRegNoFile))
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = string.Format(Localization.lookup("MissingOBJ"), Localization.lookup("CompanyRegNo"));
                    else
                        euSale.SystemInfo += Environment.NewLine + string.Format(Localization.lookup("MissingOBJ"), Localization.lookup("CompanyRegNo"));
                }
                else if (euSale._DebtorRegNoFile.Length > 12)
                {
                    hasErrors = true;
                    if (euSale.SystemInfo == VALIDATE_OK)
                        euSale.SystemInfo = string.Format(Localization.lookup("FieldTooLongOBJ"), Localization.lookup("CompanyRegNo"));
                    else
                        euSale.SystemInfo += Environment.NewLine + string.Format(Localization.lookup("FieldTooLongOBJ"), Localization.lookup("CompanyRegNo"));
                }

                if (hasErrors)
                    countErr++;
            }

            if (compressed)
            {
                listOfEU = listOfEU.Where(s => s.SystemInfo == VALIDATE_OK).ToList();
                var validateMulti = listOfEU.GroupBy(x => new { x._DebtorRegNoFile }).Where(grp => grp.Count() > 1).SelectMany(x => x);
                foreach (var rec in validateMulti)
                {
                    rec.SystemInfo = string.Format("Only one transaction per VAT-No");
                    countErr++;
                }
            }
        }

        private long StreamToFile(List<EUSaleWithoutVAT> listOfImportExport, StreamWriter sw)
        {
            long sumOfAmount = 0;
            StringBuilder sbEUList = new StringBuilder();

            foreach (var rec in listOfImportExport)
            {
                string countryStr = null;
                if (rec.Country == CountryCode.Greece)
                    countryStr = "EL";
                else
                    countryStr = Enum.GetName(typeof(CountryISOCode), ((int)rec.Country));

                var itemAmount = NumberConvert.ToLong(rec.ItemAmount);
                var serviceAmount = NumberConvert.ToLong(rec.ServiceAmount);
                var triangularTradeAmount = NumberConvert.ToLong(rec.TriangularTradeAmount);
                sumOfAmount += itemAmount + serviceAmount + triangularTradeAmount;

                rec.SystemInfo = Localization.lookup("Exported");

                sbEUList.AppendLine(string.Format("{0};{1};{2};{3};{4};{5};{6};{7};{8}",
                rec.RecordType,
                rec.ReferenceNumber,
                rec.Date.ToString("yyyy-MM-dd"),
                rec.CompanyRegNo,
                countryStr,
                rec._DebtorRegNoFile,
                itemAmount,
                triangularTradeAmount,
                serviceAmount));
            }

            sw.Write(sbEUList);

            return sumOfAmount;
        }

        private void CreateAndStreamFirstAndLast(List<EUSaleWithoutVAT> listOfEUSale, StreamWriter sw, bool firstOrLast, string companyRegNo, int countRec = 0, long sumOfAmount = 0)
        {
            StringBuilder sbEUList = new StringBuilder();

            if (firstOrLast)
            {
               sbEUList.AppendLine(string.Format("{0};{1};{2};{3}",
               "0",
               companyRegNo,
               "LISTE",
               new string(';', 5)));
            }
            else
            {
               sbEUList.AppendLine(string.Format("{0};{1};{2};{3}",
               "10",
               countRec,
               sumOfAmount,
               new string(';', 5)));
            }

            sw.Write(sbEUList);
        }

#region Estonian VIES report to XML
        public static void CreateXmlFile(Stream sfd, VD_deklaratsioon_Type declaration)
        {
            XmlDocument doc = new XmlDocument();
            XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
            ns.Add("", "");
            XmlSerializer serializer = new XmlSerializer(typeof(VD_deklaratsioon_Type), String.Empty);
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8
            };
            StringBuilder sb = new StringBuilder();
            XmlWriter xmlWriter = XmlWriter.Create(sfd, xmlWriterSettings);
            serializer.Serialize(xmlWriter, declaration, ns);
            xmlWriter.Close();
            sfd.Close();
        }

        public void CreateXml(VD_deklaratsioon_Type declaration)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.FileName = "VIES_declaration_" + declaration.perioodAasta + declaration.perioodKuu + ".xml";
            sfd.Filter = "XML failid (*.xml)|*.xml";
            var savefile = sfd.ShowDialog();
            if (savefile == DialogResult.OK)
            {
                CreateXmlFile(sfd.OpenFile(), declaration);
            }
        }

        internal void GenerateXmlReport(List<EUSaleWithoutVAT> invStats, CrudAPI api)
        {
            var declaration = new VD_deklaratsioon_Type();
            declaration.deklareerijaKood = api.CompanyEntity._VatNumber;
            declaration.perioodAasta = invStats.FirstOrDefault().Date.Year.ToString();
            declaration.perioodKuu = invStats.FirstOrDefault().Date.Month.ToString();

            declaration.aruandeRead = GenerateReportLines(invStats);
            CreateXml(declaration);
        }

        private aruandeRida_Type[] GenerateReportLines(List<EUSaleWithoutVAT> invStats)
        {
            List<aruandeRida_Type> read = new List<aruandeRida_Type>();
            foreach (var invStat in invStats)
            {
                aruandeRida_Type uusRida = new aruandeRida_Type();
                uusRida.kmkrKood = new kmkrKood_Type();
                uusRida.kmkrKood.Value = invStat.CompanyRegNo;
                invStat.SystemInfo = Uniconta.ClientTools.Localization.lookup("Exported");

                var countryCode = invStat.DebtorRef.Country;

                if (countryCode == CountryCode.Greece)
                    uusRida.kmkrKood.riik = "EL";
                else
                {
                    uusRida.kmkrKood.riik = Enum.GetName(typeof(CountryISOCode), ((int)countryCode));
                }

                uusRida.kaup = Math.Round(invStat.ItemAmount, 0).ToString();
                uusRida.kolmnurktehing = Math.Round(invStat.TriangularTradeAmount, 0).ToString();
                uusRida.teenusteMyyk = Math.Round(invStat.ServiceAmount, 0).ToString();
                read.Add(uusRida);
            }
            return read.ToArray();
        }
#endregion
    }

    public class EUSaleWithoutVATText
    {
        public static string RecordType { get { return Uniconta.ClientTools.Localization.lookup("Record"); } }
        public static string ReferenceNumber { get { return Uniconta.ClientTools.Localization.lookup("ReferenceNumber"); } }
        public static string Date { get { return Uniconta.ClientTools.Localization.lookup("Date"); } }
        public static string ItemAmount { get { return Uniconta.ClientTools.Localization.lookup("ItemAmount"); } }
        public static string ServiceAmount { get { return Uniconta.ClientTools.Localization.lookup("ServiceAmount"); } }
        public static string IsTriangularTrade { get { return Uniconta.ClientTools.Localization.lookup("TriangleTrade"); } }
        public static string Compressed { get { return Uniconta.ClientTools.Localization.lookup("Compressed"); } }
        public static string DebtorRegNo { get { return string.Format("{0} ({1})", Uniconta.ClientTools.Localization.lookup("CompanyRegNo"), Uniconta.ClientTools.Localization.lookup("Debtor")); } }
        public static string DebtorRegNoFile { get { return string.Format("{0} ({1})", Uniconta.ClientTools.Localization.lookup("CompanyRegNo"), Uniconta.ClientTools.Localization.lookup("File")); } }
        public static string TriangularTradeAmount { get { return string.Format("{0} ({1})", Uniconta.ClientTools.Localization.lookup("Amount"), Uniconta.ClientTools.Localization.lookup("TriangleTrade")); } }
    }

    [ClientTable(LabelKey = "EUSaleWithoutVAT")]
    public class EUSaleWithoutVAT: INotifyPropertyChanged, UnicontaBaseEntity
    {
        public int _CompanyId;
        public string CompanyRegNo;
        public string Filler;
        public string SystemField;
        public double TotalAmount;
        public int SalesCount;
        public string ReferenceNumber;
        public string RecordType;

        public string _Account;
        [ForeignKeyAttribute(ForeignKeyTable = typeof(Uniconta.DataModel.Debtor))]
        [Display(Name = "Account", ResourceType = typeof(DCOrderText))]
        public string Account { get { return _Account; } set { _Account = value; NotifyPropertyChanged("Account"); } }

        [Display(Name = "Name", ResourceType = typeof(GLTableText))]
        [NoSQL]
        public string DebtorName { get { return ClientHelper.GetName(_CompanyId, typeof(Uniconta.DataModel.Debtor), Account); } }

        [Display(Name = "Country", ResourceType = typeof(DCAccountText))]
        [NoSQL]
        public CountryCode Country { get { return DebtorRef._Country; } }

        public string _DebtorRegNo;
        [Display(Name = "DebtorRegNo", ResourceType = typeof(EUSaleWithoutVATText))]
        public string DebtorRegNo { get { return DebtorRef.CompanyRegNo; } }

        public string _DebtorRegNoFile;
        [Display(Name = "DebtorRegNoFile", ResourceType = typeof(EUSaleWithoutVATText))]
        public string DebtorRegNoFile { get { return _DebtorRegNoFile; } set { _DebtorRegNoFile = value; NotifyPropertyChanged("DebtorRegNoFile"); } }

        private DateTime _Date;
        [Display(Name = "Date", ResourceType = typeof(EUSaleWithoutVATText))]
        public DateTime Date
        {
            get
            {
                if (_Compressed)
                {
                    var firstDayOfMonth = new DateTime(_Date.Year, _Date.Month, 1);
                    return firstDayOfMonth.AddMonths(1).AddDays(-1);
                }
                else
                    return _Date;
            }
            set
            {
                _Date = value;
                NotifyPropertyChanged("Date");
            }
        }

        private long _InvoiceNumber;
        [Display(Name = "InvoiceNumber", ResourceType = typeof(DCInvoiceText))]
        public long InvoiceNumber { get { return _InvoiceNumber; } set { _InvoiceNumber = value; NotifyPropertyChanged("InvoiceNumber"); } }

        private double _ItemAmount;
        [Price]
        [Display(Name = "ItemAmount", ResourceType = typeof(EUSaleWithoutVATText))]
        public double ItemAmount
        {
            get
            {
                if (_Compressed)
                    return Math.Round(_ItemAmount, 0);
                else
                    return _ItemAmount;
            }
            set
            {
                _ItemAmount = value;
                NotifyPropertyChanged("ItemAmount");
            }
        }

        private double _ServiceAmount;
        [Price]
        [Display(Name = "ServiceAmount", ResourceType = typeof(EUSaleWithoutVATText))]
        public double ServiceAmount
        {
            get
            {
                if (_Compressed)
                    return Math.Round(_ServiceAmount, 0);
                else
                    return _ServiceAmount;
            }
            set
            {
                _ServiceAmount = value;
                NotifyPropertyChanged("ServiceAmount");
            }
        }

        private bool _Compressed;
        [Display(Name = "Compressed", ResourceType = typeof(EUSaleWithoutVATText))]
        public bool Compressed { get { return _Compressed; } set { _Compressed = value; NotifyPropertyChanged("Compressed"); } }

        private double _TriangularTradeAmount;
        [Price]
        [Display(Name = "TriangularTradeAmount", ResourceType = typeof(EUSaleWithoutVATText))]
        public double TriangularTradeAmount
        {
            get
            {
                if (_Compressed)
                    return Math.Round(_TriangularTradeAmount, 0);
                else
                    return _TriangularTradeAmount;
            }
            set
            {
                _TriangularTradeAmount = value;
                NotifyPropertyChanged("TriangularTradeAmount");
            }
        }

        private string _Item;
        [ForeignKeyAttribute(ForeignKeyTable = typeof(InvItem))]
        [Display(Name = "Item", ResourceType = typeof(InventoryText))]
        public string Item { get { return _Item; } set { _Item = value; NotifyPropertyChanged("Item"); } }

        [Display(Name = "ItemName", ResourceType = typeof(InvTransText))]
        [NoSQL]
        public string ItemName { get { return ClientHelper.GetName(_CompanyId, typeof(Uniconta.DataModel.InvItem), Item); } }

        private string _SystemInfo;
        [Display(Name = "SystemInfo", ResourceType = typeof(DCTransText))]
        public string SystemInfo { get { return _SystemInfo; } set { _SystemInfo = value; NotifyPropertyChanged("SystemInfo"); } }

        private bool _IsTriangularTrade;
        [Display(Name = "IsTriangularTrade", ResourceType = typeof(EUSaleWithoutVATText))]
        public bool IsTriangularTrade { get { return _IsTriangularTrade; } set { _IsTriangularTrade = value; NotifyPropertyChanged("IsTriangularTrade"); } }

        private string _Vat;
        [ForeignKeyAttribute(ForeignKeyTable = typeof(GLVat))]
        [Display(Name = "Vat", ResourceType = typeof(GLDailyJournalText)), Key]
        public string Vat { get { return _Vat; } set { _Vat = value; NotifyPropertyChanged("Vat"); } }

        [ReportingAttribute]
        public DebtorClient DebtorRef
        {
            get
            {
                return ClientHelper.GetRefClient<Uniconta.ClientTools.DataModel.DebtorClient>(_CompanyId, typeof(Uniconta.DataModel.Debtor), _Account);
            }
        }

        [ReportingAttribute]
        public InvItemClient InvItem
        {
            get
            {
                return ClientHelper.GetRefClient<InvItemClient>(_CompanyId, typeof(Uniconta.DataModel.InvItem), _Item);
            }
        }

        [ReportingAttribute]
        public GLVatClient VatRef
        {
            get
            {
                return ClientHelper.GetRefClient<GLVatClient>(_CompanyId, typeof(Uniconta.DataModel.GLVat), _Vat);
            }
        }

        [ReportingAttribute]
        public CompanyClient CompanyRef { get { return Global.CompanyRef(_CompanyId); } }

        public int CompanyId { get { return _CompanyId; } set { _CompanyId = value; } }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Type BaseEntityType() { return GetType(); }
        public void loadFields(CustomReader r, int SavedWithVersion) { }
        public void saveFields(CustomWriter w, int SaveVersion) { }
        public int Version(int ClientAPIVersion) { return 1; }
        public int ClassId() { return 37774; }
    }

    // Estonia VIES report XML schema class
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.7.2046.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://www.emta.ee/VD/xsd/webimport/v1")]
    [System.Xml.Serialization.XmlRootAttribute("VD_deklaratsioon", Namespace = "http://www.emta.ee/VD/xsd/webimport/v1", IsNullable = false)]
    public partial class VD_deklaratsioon_Type
    {

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified)]
        public string deklareerijaKood;

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified, DataType = "integer")]
        public string perioodAasta;

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified, DataType = "integer")]
        public string perioodKuu;

        /// <remarks/>
        [System.Xml.Serialization.XmlArrayAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified)]
        [System.Xml.Serialization.XmlArrayItemAttribute("aruandeRida", Form = System.Xml.Schema.XmlSchemaForm.Unqualified, IsNullable = false)]
        public aruandeRida_Type[] aruandeRead;
    }

    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.7.2046.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://www.emta.ee/VD/xsd/webimport/v1")]
    public partial class aruandeRida_Type
    {

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified)]
        public kmkrKood_Type kmkrKood;

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified, DataType = "integer")]
        public string kaup;

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified, DataType = "integer")]
        public string kolmnurktehing;

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(Form = System.Xml.Schema.XmlSchemaForm.Unqualified, DataType = "integer")]
        public string teenusteMyyk;
    }

    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.7.2046.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace = "http://www.emta.ee/VD/xsd/webimport/v1")]
    public partial class kmkrKood_Type
    {

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string riik;

        /// <remarks/>
        [System.Xml.Serialization.XmlTextAttribute()]
        public string Value;
    }
#endif
}