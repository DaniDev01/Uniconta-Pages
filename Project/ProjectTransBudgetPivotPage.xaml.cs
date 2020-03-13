using UnicontaClient.Models;
using Uniconta.ClientTools.DataModel;
using Uniconta.ClientTools.Page;
using Uniconta.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Uniconta.ClientTools.Controls;
using Uniconta.ClientTools.Util;
using UnicontaClient.Utilities;
using System.ComponentModel.DataAnnotations;
using Uniconta.ClientTools;
using Uniconta.DataModel;
using DevExpress.Xpf.Grid;
using DevExpress.Data;
using Uniconta.API.Service;
using DevExpress.Xpf.PivotGrid;
using Uniconta.Common.Utility;

using UnicontaClient.Pages;
namespace UnicontaClient.Pages.CustomPage
{
    public class ProjectTransBudgetPivotSort : IComparer<ProjectTransPivotClient>
    {
        public int Compare(ProjectTransPivotClient x, ProjectTransPivotClient y)
        {
            var c = string.Compare(x._Project, y._Project);
            if (c != 0)
                return c;
            c = DateTime.Compare(x._Date, y._Date);
            if (c != 0)
                return c;
            c = string.Compare(x._Employee, y._Employee);
            if (c != 0)
                return c;
            c = string.Compare(x._PrCategory, y._PrCategory);
            if (c != 0)
                return c;
            return string.Compare(x._PayrollCategory, y._PayrollCategory);
        }
    }
    
    public partial class ProjectTransBudgetPivotPage :  PivotBasePage
    {
        [ForeignKeyAttribute(ForeignKeyTable = typeof(Uniconta.DataModel.ProjectBudgetGroup))] 
        public ProjectBudgetGroup BudgetGroup { get; set; }

        ItemBase iIncludeSubProBase;
        static bool showBudget = true;
        static bool includeSubProject;
        static DateTime fromDate, toDate;
        static string budgetGroup;
        bool pivotIsLoaded = false;
        SQLTableCache<Uniconta.DataModel.PrCategory> prCategoryCache;
        SQLTableCache<Uniconta.DataModel.Employee> employeeCache;

        public ProjectTransBudgetPivotPage(UnicontaBaseEntity master)
            : base(master)
        {
            Init(master);
        }

        public ProjectTransBudgetPivotPage(BaseAPI API) : base(API, string.Empty)
        {
            Init(null);
        }

        private void pivotDgProjectTrans_Loaded(object sender, RoutedEventArgs e)
        {
            if (!pivotIsLoaded)
            {
                pivotDgProjectPlanning.BestFit();
                pivotIsLoaded = true;
            }
        }

        UnicontaBaseEntity master;
        private void Init(UnicontaBaseEntity _master)
        {
            InitializeComponent();
            pivotDgProjectPlanning.ShowColumnGrandTotals = true;
            pivotDgProjectPlanning.ShowRowTotals = false;
            master = _master;
            SetPageControl(pivotDgProjectPlanning, chartControl);
            layoutControl = layoutItems;
            pivotDgProjectPlanning.BusyIndicator = busyIndicator;
            ribbonControl = localMenu;
            pivotDgProjectPlanning.BeginUpdate();
            pivotDgProjectPlanning.AllowCrossGroupVariation = false;
            pivotDgProjectPlanning.api = api;
            pivotDgProjectPlanning.TableType = typeof(ProjectTransPivotClient);
            localMenu.OnItemClicked += LocalMenu_OnItemClicked;
            cmbBudgetGroup.api = api;
            SetBudgetGroup();
            localMenu.OnChecked += LocalMenu_OnChecked;
            pivotDgProjectPlanning.CellDoubleClick += PivotGridControl_CellDoubleClick;
            GetMenuItem();
            txtFromDate.DateTime = fromDate == DateTime.MinValue ? DateTime.Today : fromDate;
            txtToDate.DateTime = toDate == DateTime.MinValue ? DateTime.Today : toDate;
            cmbBudgetGroup.Text = budgetGroup;
            prCategoryCache = api.GetCache<Uniconta.DataModel.PrCategory>();
            employeeCache = api.GetCache<Uniconta.DataModel.Employee>();
            StartLoadCache();
            pivotDgProjectPlanning.CellClick += PivotDgProjectPlanning_CellClick;
            pivotDgProjectPlanning.CustomCellAppearance += PivotDgProjectPlanning_CustomCellAppearance;
            fieldQtyActualBudDiff.Caption = ProjectTransPivotClientText.QtyActualBudDiff;
            fieldQtyNormBudDiff.Caption = ProjectTransPivotClientText.QtyNormBudDiff;
            fieldQtyActualNormDiff.Caption = ProjectTransPivotClientText.QtyActualNormDiff;
            fieldSalesActualBudgetDiff.Caption = ProjectTransPivotClientText.SalesActualBudgetDiff;
            fieldCostActualBudgetDiff.Caption = ProjectTransPivotClientText.CostActualBudgetDiff;
        }

        PivotGridField[] selectedCellColumnFields = null;
        PivotGridField selectedCellRow = null;
        PivotGridField[] selectedRowFields = null;
        private void PivotDgProjectPlanning_CellClick(object sender, PivotCellEventArgs e)
        {
            selectedCellRow = e.RowField;
            selectedCellColumnFields = e.GetColumnFields();
            selectedRowFields = e.GetRowFields();
        }

        private void PivotGridControl_CellDoubleClick(object sender, DevExpress.Xpf.PivotGrid.PivotCellEventArgs e)
        {
            var cell = pivotDgProjectPlanning.FocusedCell;
            OpenBudgetTransactions(e.GetColumnFields(), e.RowField);
        }

        void OpenBudgetTransactions(PivotGridField[] columnFields, PivotGridField rowField)
        {
            var cell = pivotDgProjectPlanning.FocusedCell;
            if (cell == null || columnFields == null)
                return;
            PivotGridField monthField = null, quarterField = null, yearField = null;
            foreach (var colField in columnFields)
            {
                if (colField.GroupInterval == FieldGroupInterval.DateMonth)
                    monthField = colField;
                if (colField.GroupInterval == FieldGroupInterval.DateQuarter)
                    quarterField = colField;
                if (colField.GroupInterval == FieldGroupInterval.DateYear)
                    yearField = colField;
            }

            bool isRowSelected = rowField.FieldName == "EmployeeName" || rowField.FieldName == "Employee" ||
                rowField.FieldName == "ProjectName" || rowField.FieldName == "Project" ||
                rowField.FieldName == "PrCategory" || rowField.FieldName == "PayrollCategory" ||
                rowField.FieldName == "EmplDim1" || rowField.FieldName == "EmplDim2" || rowField.FieldName == "EmplDim3"
                || rowField.FieldName == "EmplDim4" || rowField.FieldName == "EmplDim5" ||
                rowField.FieldName == "ProjDim1" || rowField.FieldName == "ProjDim2" || rowField.FieldName == "ProjDim3"
                || rowField.FieldName == "ProjDim4" || rowField.FieldName == "ProjDim5";

            if (((monthField != null || quarterField != null || yearField != null) && isRowSelected) || isRowSelected)
            {
                if(rowField.FieldName == "EmployeeName")
                {
                   var empRow = selectedRowFields?.FirstOrDefault(x => x.FieldName == "Employee");
                    if (empRow == null)
                        return;
                    else
                        rowField = empRow;
                }
                else if(rowField.FieldName == "ProjectName")
                {
                    var projRow = selectedRowFields?.FirstOrDefault(x => x.FieldName == "Project");
                    if (projRow == null)
                        return;
                    else
                        rowField = projRow;
                }

                object monthfldValue = monthField != null ? pivotDgProjectPlanning.GetFieldValue(monthField, cell.X) : null;
                object qrtfldValue = quarterField != null ? pivotDgProjectPlanning.GetFieldValue(quarterField, cell.X) : null;
                object yearfldValue = yearField != null ? pivotDgProjectPlanning.GetFieldValue(yearField, cell.X) : null;
                object rowValue = pivotDgProjectPlanning.GetFieldValue(rowField, cell.Y);

                string filterFldName = rowField.FieldName;
                string filterFldValue = (string)rowValue;

                var monthNo = monthfldValue != null ? (int)monthfldValue : 0; 
                var quarterNo = qrtfldValue != null ? (int)qrtfldValue : 0;
                var year = yearfldValue != null ? (int)yearfldValue : 0;

                var selectedcell = pivotDgProjectPlanning.SelectedCellInfo.DrillDownDataSource?.Cast<ProjectTransPivotClient>();
                double budQty = selectedcell.Sum(x => x._BudgetQty);
                double normQty = selectedcell.Sum(x => x._NormQty);

                var projectTrans = selectedcell?.FirstOrDefault();

                DateTime cellFromDate = DateTime.MinValue;
                DateTime cellToDate = DateTime.MaxValue;
                if (projectTrans != null)
                {
                    if (monthNo != 0)
                    {
                        cellFromDate = new DateTime(projectTrans._Date.Year, monthNo, 1);
                        cellToDate = cellFromDate.AddMonths(1).AddDays(-1);

                        if (fromDate.Month == monthNo && fromDate > cellFromDate) cellFromDate = fromDate;
                        if (toDate.Month == monthNo && toDate < cellToDate) cellToDate = toDate;
                    }
                    else if (quarterNo != 0)
                    {
                        var firstMth = 3 * quarterNo - 2;

                        cellFromDate = new DateTime(projectTrans._Date.Year, firstMth, 1);
                        cellToDate = cellFromDate.AddMonths(3).AddDays(-1);

                        int quarterFromDate = (fromDate.Month + 2) / 3;
                        if (quarterFromDate == quarterNo && fromDate > cellFromDate) cellFromDate = fromDate;
                        if (toDate.Month == cellToDate.Month && toDate < cellToDate) cellToDate = toDate;

                    }
                    else if (year != 0)
                    {
                        cellFromDate = new DateTime(year, 1, 1);
                        cellToDate = cellFromDate.AddYears(1).AddDays(-1);

                        if (fromDate.Year == year && fromDate > cellFromDate) cellFromDate = fromDate;
                        if (toDate.Year == year && toDate < cellToDate) cellToDate = toDate;
                    }
                    else
                    {
                        cellFromDate = fromDate;
                        cellToDate = toDate;
                    }
                }

                string vheader = string.Format("{0} {1} ({2})", Uniconta.ClientTools.Localization.lookup("ProjectPlanning"), Uniconta.ClientTools.Localization.lookup("Lines"), filterFldValue);
                var param = new object[5];
                param[0] = new string[] { filterFldName, filterFldValue } ;
                param[1] = new int[] { monthNo, quarterNo, year };
                param[2] = new double[] { normQty, budQty };
                param[3] = new DateTime[] { cellFromDate.Date, cellToDate.Date };
                param[4] = api;
                AddDockItem(TabControls.ProjectBudgetPlaningLinePage, param, vheader);
            }
        }

        private async void SetBudgetGroup()
        {
            var budgetGroup = await api.Query<ProjectBudgetGroup>();
            cmbBudgetGroup.ItemsSource = budgetGroup;
        }

        private void LocalMenu_OnChecked(string actionType, bool IsChecked)
        {
            includeSubProject = IsChecked;
            ShowInculdeSubProject();
        }

        class SortNormCalendar : IComparer<CalenderNormLst>
        {
            public int Compare(CalenderNormLst x, CalenderNormLst y)
            {
                var c = string.Compare(x.CalendarId, y.CalendarId);
                if (c != 0)
                    return c;
                return DateTime.Compare(x.Date, y.Date);
            }
        }

        class SortCalendarSetup : IComparer<TMEmpCalendarSetupClient>
        {
            public int Compare(TMEmpCalendarSetupClient x, TMEmpCalendarSetupClient y)
            {
                return string.Compare(x.Employee, y.Employee);
            }
        }

        bool labelVisibility = true;
        bool chartEnable = true;
        int seriesIndex = 0;
        protected override void OnLayoutLoaded()
        {
            base.OnLayoutLoaded();
            Utility.SetDimensionsPivotGrid(api, fieldEmplDim1, fieldEmplDim2, fieldEmplDim3, fieldEmplDim4, fieldEmplDim5);
            fieldEmplDim1.Caption = string.Concat(fieldEmplDim1, " (", Uniconta.ClientTools.Localization.lookup("Employee"), ")");
            fieldEmplDim2.Caption = string.Concat(fieldEmplDim2, " (", Uniconta.ClientTools.Localization.lookup("Employee"), ")");
            fieldEmplDim3.Caption = string.Concat(fieldEmplDim3, " (", Uniconta.ClientTools.Localization.lookup("Employee"), ")");
            fieldEmplDim4.Caption = string.Concat(fieldEmplDim4, " (", Uniconta.ClientTools.Localization.lookup("Employee"), ")");
            fieldEmplDim5.Caption = string.Concat(fieldEmplDim5, " (", Uniconta.ClientTools.Localization.lookup("Employee"), ")");

            Utility.SetDimensionsPivotGrid(api, fieldProjDim1, fieldProjDim2, fieldProjDim3, fieldProjDim4, fieldProjDim5);
            fieldProjDim1.Caption = string.Concat(fieldProjDim1, " (", Uniconta.ClientTools.Localization.lookup("Project"), ")");
            fieldProjDim2.Caption = string.Concat(fieldProjDim2, " (", Uniconta.ClientTools.Localization.lookup("Project"), ")");
            fieldProjDim3.Caption = string.Concat(fieldProjDim3, " (", Uniconta.ClientTools.Localization.lookup("Project"), ")");
            fieldProjDim4.Caption = string.Concat(fieldProjDim4, " (", Uniconta.ClientTools.Localization.lookup("Project"), ")");
            fieldProjDim5.Caption = string.Concat(fieldProjDim5, " (", Uniconta.ClientTools.Localization.lookup("Project"), ")");

            if (chartControl.Diagram != null)
            {
                chartControl.Visibility = Visibility.Visible;
                labelVisibility = chartControl.Diagram.SeriesTemplate.LabelsVisibility;
                seriesIndex = GetSeriesId();
            }
            if (master == null)
            {
                fieldProject.Visible = true;
                fieldProjectName.Visible = true;
            }
            if (showBudget)
                return;
            fieldBudgetQty.Visible = false;
            fieldBudgetCost.Visible = false;
            fieldBudgetSales.Visible = false;
        }
       
        void GetMenuItem()
        {
            RibbonBase rb = (RibbonBase)localMenu.DataContext;
            iIncludeSubProBase = UtilDisplay.GetMenuCommandByName(rb, "InclSubProjects");
        }

        class CalenderNormLst
        {
            public string CalendarId;
            public DateTime Date;
            public double NormQty;
        }

        private static string FieldCannotBeEmpty(string field)
        {
            return String.Format("{0} ({1}: {2})",
                   Uniconta.ClientTools.Localization.lookup("FieldCannotBeEmpty"),
                   Uniconta.ClientTools.Localization.lookup("Field"), field);
        }

        async Task LoadGrid()
        {
            fromDate = txtFromDate.DateTime;
            toDate = txtToDate.DateTime;
            budgetGroup = cmbBudgetGroup.Text;

            if (string.IsNullOrEmpty(budgetGroup))
            {
                var msgText = FieldCannotBeEmpty(Uniconta.ClientTools.Localization.lookup("BudgetGroup"));
                UnicontaMessageBox.Show(msgText, Uniconta.ClientTools.Localization.lookup("Warning"), MessageBoxButton.OK);
                return;
            }

            if (fromDate == DateTime.MinValue)
            {
                var msgText = FieldCannotBeEmpty(Uniconta.ClientTools.Localization.lookup("FromDate"));
                UnicontaMessageBox.Show(msgText, Uniconta.ClientTools.Localization.lookup("Warning"), MessageBoxButton.OK);
                return;
            }

            if (toDate == DateTime.MinValue)
            {
                var msgText = FieldCannotBeEmpty(Uniconta.ClientTools.Localization.lookup("ToDate"));
                UnicontaMessageBox.Show(msgText, Uniconta.ClientTools.Localization.lookup("Warning"), MessageBoxButton.OK);
                return;
            }

            busyIndicator.IsBusy = true;


            List<PropValuePair> filter = new List<PropValuePair>();
            if (fromDate != DateTime.MinValue)
            {
                var propValuePairFolder = PropValuePair.GenereteParameter("FromDate", typeof(string), Convert.ToString(fromDate.Ticks));
                filter.Add(propValuePairFolder);
            }
            if (toDate != DateTime.MinValue)
            {
                var propValuePairFolder = PropValuePair.GenereteParameter("ToDate", typeof(string), Convert.ToString(toDate.Ticks));
                filter.Add(propValuePairFolder);
            }

            if (budgetGroup != null)
            {
                var propValuePairFolder = PropValuePair.GenereteParameter("BudgetGroup", typeof(string), budgetGroup);
                filter.Add(propValuePairFolder);
            }

            var api = this.api;
            var CompanyId = api.CompanyId;

            var transTask = api.Query(new ProjectTransPivotClient(), new List<UnicontaBaseEntity>() { master} , filter);

            var trans = await transTask;
            if (trans == null)
            {
                return;
            }

            var len = trans.Length;
            var sort = new ProjectTransBudgetPivotSort();
            Array.Sort(trans, sort);

            List<ProjectTransPivotClient> extras = null;
           
            if (showBudget)
            {
                var budget = await api.Query(new ProjectBudgetPivotClient(), new List<UnicontaBaseEntity>() { master }, filter);
                var key = new ProjectTransPivotClient();
                foreach (var bc in budget)
                {
                    key._Project = bc._Project;
                    key._Date = bc._Date;
                    key._PrCategory = bc._PrCategory;
                    key._PayrollCategory = bc._PayrollCategory;
                    key._Employee = bc.Employee;
                    var idx = Array.BinarySearch(trans, key, sort);
                    if (idx >= 0 && idx < len)
                    {
                        var t = trans[idx];
                        t._BudgetSales += bc._Sales;
                        t._BudgetCost += bc._Cost;
                        t._BudgetQty += bc._Qty;
                    }
                    else
                    {
                        var prTrans = new ProjectTransPivotClient()
                        {
                            _CompanyId = CompanyId,
                            _BudgetSales = bc._Sales,
                            _BudgetCost = bc._Cost,
                            _BudgetQty = bc._Qty,
                            _PrCategory = bc._PrCategory,
                            _Project = bc._Project,
                            _Date = bc._Date,
                            _Employee = bc._Employee,
                            _PayrollCategory = bc._PayrollCategory
                        };
                                                    
                        if (extras == null)
                            extras = new List<ProjectTransPivotClient>();
                        extras.Add(prTrans);
                    }
                }

                if (extras != null)
                {
                    Array.Resize(ref trans, len + extras.Count);
                    foreach (var sum in extras)
                        trans[len++] = sum;
                    Array.Sort(trans, sort);
                    extras = null;
                }
            }

            int start = 0;
            while (start < len)
            {
                int end;
                string ProjectNumber;
                if (master == null)
                {
                    ProjectNumber = trans[start]._Project;
                    for (end = start; (end < len && trans[end]._Project == ProjectNumber); end++)
                        ;
                }
                else
                {
                    ProjectNumber = ((Uniconta.DataModel.Project)master)._Number;
                    end = len;
                }
   
                start = end;
            }

            #region Norm Calendar
            CalenderNormLst[] normLst = null;

            var pairCalendarLine = new PropValuePair[]
            {
                PropValuePair.GenereteWhereElements(nameof(TMEmpCalendarLineClient.Date), typeof(DateTime), String.Format("{0:d}..{1:d}", fromDate, toDate))
            };

            var tmEmpCalenderLineLst = await api.Query<TMEmpCalendarLineClient>(pairCalendarLine);
            if (tmEmpCalenderLineLst.Length > 0)
            {

                var grpCalendarLst = tmEmpCalenderLineLst.GroupBy(x => new { x.Calendar, x.FirstDayOfMonth }).Select(x => new { Key = x.Key, Hours = x.Sum(y => y.Hours) }).ToList();


                foreach (var rec in grpCalendarLst)
                {
                    var normTrans = new CalenderNormLst()
                    {
                        CalendarId = rec.Key.Calendar,
                        Date = rec.Key.FirstDayOfMonth,
                        NormQty = rec.Hours
                    };

                    if (normLst == null)
                        normLst = new CalenderNormLst[] { normTrans };
                    else
                    {
                        Array.Resize(ref normLst, normLst.Length + 1);
                        normLst[normLst.Length - 1] = normTrans;
                    }
                }

                var normCalSort = new SortNormCalendar();
                Array.Sort(normLst, normCalSort);

                var lstCalendarSetup = await api.Query<TMEmpCalendarSetupClient>();

                var calSetupSort = new SortCalendarSetup();
                Array.Sort(lstCalendarSetup, calSetupSort);

                var calenders = new List<TMEmpCalendarSetupClient>(10);
                var searchCalSetup = new TMEmpCalendarSetupClient();

                var empNormLst = new List<ProjectTransPivotClient>();
                var searchNorm = new CalenderNormLst();

                foreach (var empl in employeeCache)
                {
                    var curEmployee = empl._Number;

                    calenders.Clear();

                    searchCalSetup.Employee = curEmployee;
                    var posCalSetup = Array.BinarySearch(lstCalendarSetup, searchCalSetup, calSetupSort);
                    if (posCalSetup < 0)
                        posCalSetup = ~posCalSetup;
                    while (posCalSetup < lstCalendarSetup.Length)
                    {
                        var s = lstCalendarSetup[posCalSetup++];
                        if (s.Employee != curEmployee)
                            break;

                        calenders.Add(s);
                    }

                    if (calenders.Count == 0)
                        continue;
                    
                    foreach (var rec in calenders.OrderBy(s => s.ValidFrom))
                    {
                        var newStartDate = rec._ValidFrom != DateTime.MinValue && rec._ValidFrom > fromDate ? rec._ValidFrom : fromDate;
                        var empStartDate = empl._Hired == DateTime.MinValue ? newStartDate : empl._Hired > newStartDate ? empl._Hired : newStartDate;

                        var newEndDate = rec._ValidTo != DateTime.MinValue && rec._ValidTo < toDate ? rec._ValidTo : toDate;
                        var empEndDate = empl._Terminated == DateTime.MinValue ? newEndDate : empl._Terminated < newEndDate ? empl._Terminated : newEndDate;

                        var empFirstDayOfMonth = FirstDayOfMonth(empStartDate);
                        var empLastDayOfMonth = LastDayOfMonth(empEndDate);

                        int empFirstMth = 0, empLastMth = 0;

                        if (empFirstDayOfMonth != empStartDate)
                            empFirstMth = empStartDate.Month;

                        if (empLastDayOfMonth != empEndDate)
                            empLastMth = empEndDate.Month;

                        searchNorm.CalendarId = rec._Calendar;
                        searchNorm.Date = empFirstDayOfMonth;

                        var pos = Array.BinarySearch(normLst, searchNorm, normCalSort);
                        if (pos < 0)
                            pos = ~pos;
                        while (pos < normLst.Length)
                        {
                            var s = normLst[pos++];
                            if (s.CalendarId != rec._Calendar || s.Date > empEndDate)
                                break;


                            if (empFirstMth != 0 && s.Date.Month == empFirstMth)
                            {
                                var firstDayOfMonth = FirstDayOfMonth(empStartDate);
                                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                                var hours = tmEmpCalenderLineLst.Where(x => x._Calendar == s.CalendarId && x.Date >= empStartDate && x.Date <= lastDayOfMonth).Sum(y => y._Hours);

                                var newTrans = new ProjectTransPivotClient()
                                {
                                    _CompanyId = CompanyId,
                                    _Employee = curEmployee,
                                    _Date = firstDayOfMonth,
                                    _NormQty = hours
                                };

                                empNormLst.Add(newTrans);
                            }
                            else if (empLastMth != 0 && s.Date.Month == empLastMth)
                            {
                                var firstDayOfMonth = FirstDayOfMonth(empEndDate);
                                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                                var hours = tmEmpCalenderLineLst.Where(x => x._Calendar == s.CalendarId && x.Date >= firstDayOfMonth && x.Date <= empEndDate).Sum(y => y._Hours);

                                var newTrans = new ProjectTransPivotClient()
                                {
                                    _CompanyId = CompanyId,
                                    _Employee = curEmployee,
                                    _Date = firstDayOfMonth,
                                    _NormQty = hours
                                };

                                empNormLst.Add(newTrans);

                            }
                            else if (s.Date >= empStartDate && s.Date <= empEndDate)
                            {
                                var newTrans = new ProjectTransPivotClient()
                                {
                                    _CompanyId = CompanyId,
                                    _Employee = curEmployee,
                                    _Date = s.Date,
                                    _NormQty = s.NormQty
                                };

                                empNormLst.Add(newTrans);
                            }
                        }
                    }
                }
                if (empNormLst.Count > 0)
                {
                    Array.Resize(ref trans, len + empNormLst.Count);
                    foreach (var norm in empNormLst)
                        trans[len++] = norm;

                    Array.Sort(trans, sort);
                }
            }
            #endregion Norm Calendar

            pivotDgProjectPlanning.DataSource = trans as IList;
            if (!isPivotIsVisible)
            {
                pivotDgProjectPlanning.EndUpdate();
                pivotDgProjectPlanning.Visibility = Visibility.Visible;
                isPivotIsVisible = true;
            }
            pivotDgProjectPlanning.RefreshData();
            busyIndicator.IsBusy = false;
        }

        bool isPivotIsVisible = false;

        private DateTime FirstDayOfMonth(DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }

        private DateTime LastDayOfMonth(DateTime date)
        {
            return FirstDayOfMonth(date).AddMonths(1).AddDays(-1);
        }

        private void LocalMenu_OnItemClicked(string ActionType)
        {
            switch (ActionType)
            {
                case "Search":
                    BtnSearch();

                    break;
                case "RefreshGrid":
                    BtnSearch();
                    break;

                case "ChartSettings":

                    CWChartSettings cwChartSettingDialog = new CWChartSettings(pivotDgProjectPlanning.ChartSelectionOnly, pivotDgProjectPlanning.ChartProvideColumnGrandTotals,
                        pivotDgProjectPlanning.ChartProvideRowGrandTotals, labelVisibility, seriesIndex, pivotDgProjectPlanning.ChartProvideDataByColumns, chartEnable);
                    cwChartSettingDialog.Closed += delegate
                    {
                        if (cwChartSettingDialog.DialogResult == true)
                        {
                            if (cwChartSettingDialog.IsChartVisible)
                            {
                                chartControl.Diagram = cwChartSettingDialog.ChartDaigram;
                                pivotDgProjectPlanning.ChartProvideEmptyCells = IsPivotTableProvideEmptyCells();
                                chartControl.Diagram.SeriesTemplate.LabelsVisibility = cwChartSettingDialog.labelVisibility;
                                chartControl.CrosshairEnabled = cwChartSettingDialog.crossHairEnabled;
                                pivotDgProjectPlanning.ChartProvideDataByColumns = cwChartSettingDialog.chartProvideDataByColumns;
                                pivotDgProjectPlanning.ChartSelectionOnly = cwChartSettingDialog.ChartSelectionOnly;
                                pivotDgProjectPlanning.ChartProvideColumnGrandTotals = cwChartSettingDialog.ChartProvideColumnGrandTotals;
                                pivotDgProjectPlanning.ChartProvideRowGrandTotals = cwChartSettingDialog.ChartProvideRowGrandTotals;
                                chartControl.Visibility = Visibility.Visible;
                                if (rowgridSplitter.Height.Value == 0 && rowChartControl.Height.Value == 0)
                                {
                                    rowgridSplitter.Height = new GridLength(5);
                                    var converter = new GridLengthConverter();
                                    rowChartControl.Height = (GridLength)converter.ConvertFrom("Auto");
                                }
                            }
                            else
                            {
                                chartControl.Visibility = Visibility.Collapsed;
                                rowgridSplitter.Height = new GridLength(0);
                                rowChartControl.Height = new GridLength(0);
                            }
                            chartEnable = cwChartSettingDialog.IsChartVisible;
                            labelVisibility = cwChartSettingDialog.labelVisibility;
                        }
                    };
                    cwChartSettingDialog.Show();
                    break;
                case "Lines":
                    if (selectedCellRow != null && selectedCellColumnFields != null)
                        OpenBudgetTransactions(selectedCellColumnFields, selectedCellRow);
                    break;
                default:
                    controlRibbon_BaseActions(ActionType);
                    break;
            }
        }

        Task BtnSearch()
        {
            return LoadGrid();
        }

        Task ShowInculdeSubProject()
        {
            iIncludeSubProBase.IsChecked = includeSubProject;
            return LoadGrid();
        }
       
        protected override async void LoadCacheInBackGround()
        {
            if (prCategoryCache == null)
                prCategoryCache = await api.LoadCache<Uniconta.DataModel.PrCategory>().ConfigureAwait(false);
            if (employeeCache == null)
                employeeCache = await api.LoadCache<Uniconta.DataModel.Employee>().ConfigureAwait(false);
        }

        private void pivotDgProjectPlanning_CustomSummary(object sender, PivotCustomSummaryEventArgs e)
        {
            string name = e.DataField.Name;
            if (name == "fieldQtyActualBudDiff")
                e.CustomValue = CalculateQty("Qty", "BudgetQty", e.CreateDrillDownDataSource());
            else if (name == "fieldQtyNormBudDiff")
                e.CustomValue = CalculateQty("NormQty", "BudgetQty", e.CreateDrillDownDataSource());
            else if (name == "fieldQtyActualNormDiff")
                e.CustomValue = CalculateQty("Qty", "NormQty", e.CreateDrillDownDataSource());
            else if (name == "fieldSalesActualBudgetDiff")
                e.CustomValue = CalculateQty("Sales", "BudgetSales", e.CreateDrillDownDataSource());
            else if (name == "fieldCostActualBudgetDiff")
                e.CustomValue = CalculateQty("Cost", "BudgetCost", e.CreateDrillDownDataSource());
            else if (name == "fieldPercentage")
                e.CustomValue = CalculatePercentage("NormQty", "BudgetQty", e.CreateDrillDownDataSource());
        }

        double CalculatePercentage(string field1, string field2, IList list)
        {
            double val = 0;
            double normQty = 0;
            double diff = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i] as DevExpress.XtraPivotGrid.PivotDrillDownDataRow;
                object Qty1 = row[field1];
                object Qty2 = row[field2];
                if (Qty1 != null && Qty1 != DBNull.Value && Qty2 != null && Qty2 != DBNull.Value)
                {
                    diff = (Convert.ToDouble(Qty1) - Convert.ToDouble(Qty2)) + diff;
                    normQty = Convert.ToDouble(Qty1) + normQty;
                }
            }
            val = diff / normQty * 100;
            if (double.IsNegativeInfinity(val) || double.IsPositiveInfinity(val) || (diff ==0d && normQty==0d))
                val = 0d;
            return val;
        }

        double CalculateQty(string field1, string field2, IList list)
        {
            double val = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i] as DevExpress.XtraPivotGrid.PivotDrillDownDataRow;
                object Qty1 = row[field1];
                object Qty2 = row[field2];
                if (Qty1 != null && Qty1 != DBNull.Value && Qty2 != null && Qty2 != DBNull.Value)
                    val = (Convert.ToDouble(Qty1) - Convert.ToDouble(Qty2)) + val;
            }
            return val;
        }

        private void PivotDgProjectPlanning_CustomCellAppearance(object sender, PivotCustomCellAppearanceEventArgs e)
        {
            var name = e.DataField.Name;
            if (name == "fieldPercentage")
            {
                if(Convert.ToDouble(e.Value) < - 20.00)
                e.Background = new SolidColorBrush(Colors.LightPink);
                else if (Convert.ToDouble(e.Value) > 20.00)
                    e.Background = new SolidColorBrush(Colors.LightYellow);
                else if (Convert.ToDouble(e.Value) >= -20.00 || Convert.ToDouble(e.Value) <= 20.00)
                    e.Background = new SolidColorBrush(Colors.LightGreen);
            }
        }
    }
}

