﻿using OfficeOpenXml;
using OfficeOpenXml.Drawing;
using Prism.Commands;
using Prism.Mvvm;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;


namespace ThumbnailedBOM.ViewModels
{
    public class MainViewModel : BindableBase
    {

      
        Window window;
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private string message = "Set the save location to start...";

        private Font font;
        public Font Font
        {
            get { return font; }
            set { SetProperty(ref font, value); }
        }

        public string Message
        {
           
            get { return message; }
            set { SetProperty(ref message, value); }
        }
        private string saveLocation;
        public string SaveLocation
        {
            get { return saveLocation; }
            set { SetProperty(ref saveLocation, value); }
        }

        

        private bool isIdle = true;
        public bool IsIdle
        {
            get { return isIdle; }
            set { SetProperty(ref isIdle, value); }
        }

        private DelegateCommand about;
        public DelegateCommand About =>
            about ?? (about = new DelegateCommand(ExecuteAbout, CanExecuteAbout));

        void ExecuteAbout()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Exports SOLIDWORKS BOM to excel with thumbnails. Limitations:");
            stringBuilder.AppendLine("- Works best on parts only BOM.");
            stringBuilder.AppendLine("- Thumbnails are not necessarily latest.");
            stringBuilder.AppendLine("- Use default excel font / Does not propogate SOLIDWORKS BOM style.");
            stringBuilder.AppendLine("- Thumbnails are dimensioned at 30*30.");
            stringBuilder.AppendLine("Program licensed under MIT License.");
            stringBuilder.AppendLine("Developed by Amen Jlili - https://github.com/jliliamen");
            System.Windows.Forms.MessageBox.Show(stringBuilder.ToString(), AddInContext.AddInName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }

        bool CanExecuteAbout()
        {
            return IsIdle;
        }

        private DelegateCommand donate;
        public DelegateCommand Donate =>
            donate ?? (donate = new DelegateCommand(ExecuteDonate, CanExecuteDonate));

       
        private DelegateCommand cancel;
        public DelegateCommand Cancel =>
            cancel ?? (cancel = new DelegateCommand(ExecuteCancel, CanExecuteCancel));
    
        private DelegateCommand start;
        public DelegateCommand Start =>
            start ?? (start = new DelegateCommand(ExecuteStart, CanExecuteStart));
       
        private DelegateCommand setSaveLocation;
        public DelegateCommand SetSaveLocation =>
            setSaveLocation ?? (setSaveLocation = new DelegateCommand(ExecuteSetSaveLocation, CanExecuteSetSaveLocation));

        
        public MainViewModel()
        {
            window = Application.Current.MainWindow;
            Start.ObservesProperty(() => this.SaveLocation);
            Start.ObservesProperty(() => this.IsIdle);
            SetSaveLocation.ObservesProperty(() => this.IsIdle);
            Donate.ObservesProperty(() => this.IsIdle);
            Cancel.ObservesProperty(() => this.IsIdle);
            About.ObservesProperty(() => this.IsIdle);
        }

        #region Execute and CanExecute
        void ExecuteSetSaveLocation()
        {
            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Excel files | *.xlsx";
            if (save.ShowDialog() == DialogResult.OK)
            {
                var ret = save.FileName;
                SaveLocation = ret;
            }
        }

        bool CanExecuteSetSaveLocation()
        {
            return IsIdle;
        }
        void ExecuteDonate()
        {
            Process.Start("https://www.paypal.me/AmenAllahJLILI");
        }

        bool CanExecuteDonate()
        {
            return IsIdle;
        }
        void ExecuteCancel()
        {
            cancellationTokenSource.Cancel();
            this.Message = "Cancel request received. Please wait..."; 
            
        }

        bool CanExecuteCancel()
        {
            return  !IsIdle;
        }
        async void ExecuteStart()
        {
            IsIdle = false;
            cancellationTokenSource = new CancellationTokenSource();
            var modelDoc = AddInContext.SOLIDWORKS.ActiveDoc as ModelDoc2; 
            if (modelDoc != null)
            {
                if (modelDoc.GetType() == (int)swDocumentTypes_e.swDocDRAWING)
                {
                    var selectionManager = modelDoc.SelectionManager as SelectionMgr;
                    int count = selectionManager.GetSelectedObjectCount2(-1);
                    if (count > 0)
                    {
                        bool found = false;
                        // Note: will traverse all selected tables and process last.
                        // needs to be extended to only process one table or all tables.
                        for (int i = 1; i < count+1; i++)
                        {
                            
                            Debug.Print(selectionManager.GetSelectedObjectType3(i, -1).ToString());

                            if (selectionManager.GetSelectedObjectType3(i, -1) == (int)swSelectType_e.swSelANNOTATIONTABLES)
                            {
                                found = true; 
                                var tableAnnotation = selectionManager.GetSelectedObject6(i, -1) as TableAnnotation;
                                BomTableAnnotation bomTableAnnotation;
                                bomTableAnnotation = tableAnnotation as BomTableAnnotation;

                                TableBoundryCondition tableBoundryConditions = new TableBoundryCondition();

                                if (bomTableAnnotation != null)
                                {
                                    swTableHeaderPosition_e tableHeaderPosition = (swTableHeaderPosition_e)tableAnnotation.GetHeaderStyle();


                                    tableBoundryConditions.RowHeaderIndex = 0;
                                    tableBoundryConditions.StartIndex = 1;
                                    tableBoundryConditions.EndIndex = tableAnnotation.RowCount - 1;
                                    tableBoundryConditions.HeaderPosition = swTableHeaderPosition_e.swTableHeader_Top;

                                    if (tableHeaderPosition == swTableHeaderPosition_e.swTableHeader_Bottom)
                                    {
                                        tableBoundryConditions.RowHeaderIndex = tableAnnotation.RowCount - 1;
                                        tableBoundryConditions.StartIndex = 0;
                                        tableBoundryConditions.EndIndex = tableAnnotation.RowCount - 2;
                                        tableBoundryConditions.HeaderPosition = swTableHeaderPosition_e.swTableHeader_Bottom;
                                    }

                                    var processRet = await ProcessTableAsync(bomTableAnnotation, tableAnnotation, tableBoundryConditions, cancellationTokenSource);

                                    if (processRet.Item1)
                                    {
                                        var dialogRet = System.Windows.Forms.MessageBox.Show("Would you like to open the export BOM?", AddInContext.AddInName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                                        if (dialogRet == DialogResult.Yes)
                                        Process.Start(saveLocation);
                                    }
                                    else
                                    {
                                        // case of an error 
                                        this.Message = processRet.Item2; 
                                    }

                                }
                                else
                                {
                                    Message = "Table is not of type Bill of Materials.";
                                }
                            }
                        }
                        if (found == false)
                            Message = "No table was selected.";
                    }
                    else
                    {
                        Message = "No table was selected.";
                    }
                }
            }
            Message = "Completed.";
            IsIdle = true;
        }
       
        Tuple<bool,string> ProcessTable(BomTableAnnotation bomTable, TableAnnotation table, TableBoundryCondition tableCondition, CancellationTokenSource source)
        {
            try
            {
                var bomFile = new FileInfo(SaveLocation);
                if (bomFile.Exists)
                {
                    SendMessageToUI($"Deleting {SaveLocation}...");
                    bomFile.Delete();
                }

                using (var p = new ExcelPackage(bomFile))
                {


                    int Height = 30;
                    int Width = 30;
                    double ColWidth = 0;
                    //Get the Worksheet created in the previous codesample. 
                    var ws = p.Workbook.Worksheets.Add("BOM");


                    

                    for (int i = tableCondition.StartIndex; i <= tableCondition.EndIndex; i++)
                    {
                        if (source.Token.IsCancellationRequested)
                        {
                            p.Save();
                            return new Tuple<bool, string>(false, "Cancelled by user.");
                        }

                        if (table.RowHidden[i])
                            continue;

                        string partNumber = string.Empty;
                        string itemNumber = string.Empty;
                        if (bomTable.GetComponentsCount2(i, string.Empty, out itemNumber, out partNumber) > 0)
                        {

                            var components = (object[])bomTable.GetComponents2(i, string.Empty);
                            var swComponent = components.First() as Component2;
                            var modelDoc = swComponent.GetModelDoc2() as ModelDoc2;
                            if (modelDoc != null)
                            {

                                var modelDocTitle = Path.GetFileNameWithoutExtension(modelDoc.GetTitle());
                                SendMessageToUI($"{i}/{tableCondition.EndIndex} - creating a thumbnail for{modelDocTitle}...");
                                var referencedConfiguration = swComponent.ReferencedConfiguration;
                                var configuration = modelDoc.GetActiveConfiguration() as Configuration;
                                if (configuration != null)
                                {
                                    string configurationName = configuration.Name;
                                    if (configurationName != referencedConfiguration)
                                        modelDoc.ShowConfiguration2(referencedConfiguration);
                                }


                                object dispatchImg = null;
                                try
                                {
                                    DoSomethingInMainThread(() => 
                                    {
                                        dispatchImg = AddInContext.SOLIDWORKS.GetPreviewBitmap(modelDoc.GetPathName(), swComponent.ReferencedConfiguration);
                                    });
                                }
                                catch (Exception e)
                                {
                                    Debug.Print(e.Message);
                                }
                               
                                if (dispatchImg != null)
                                {
                                    ws.Row(i + 1).Height = ExcelHelper.PixelHeightToExcel(Height);
                                    var bitmap = PictureHelper.Convert(dispatchImg);
                                    var image = bitmap as Image;
                                    ExcelPicture pic = ws.Drawings.AddPicture(i.ToString(), image);
                                    pic.SetPosition(i, 0, 0, 0);
                                    pic.SetSize(Height, (int)Width);
                                }
                                else
                                {
                                    ws.Row(i + 1).Height = ExcelHelper.PixelHeightToExcel(Height);
                                    ws.Cells[i + 1, 1].Value = "N/A";
                                }

                                for (int j = 0; j < table.ColumnCount - 1; j++)
                                {
                                    if (table.ColumnHidden[j])
                                        continue;

                                    ws.Cells[i + 1, j + 2].Value = table.DisplayedText[i, j];
                                }

                                modelDoc.Visible = false;
                            }
                            else
                            {
                                ws.Row(i + 1).Height = ExcelHelper.PixelHeightToExcel(Height);
                                ws.Cells[i+1, 1].Value = "N/A";
                            }
                        }
                        else
                        {
                            ws.Row(i + 1).Height = ExcelHelper.PixelHeightToExcel(Height);
                            ws.Cells[i + 1, 1].Value = "N/A";
                            ws.Cells[i + 1, 2, i + 1, table.ColumnCount].Merge = true;
                            ws.Cells[i + 1, 2, i + 1, table.ColumnCount].Value = "Failed To get row values. API Error.";
                        }
                       
                    }

                    // add row headers
                    for (int k = 0; k < table.ColumnCount -1; k++)
                    {
                        if (table.ColumnHidden[k])
                            continue;
                        ws.Cells[tableCondition.RowHeaderIndex+1, k + 2].Value = table.DisplayedText[tableCondition.RowHeaderIndex, k];
                        ws.Cells[tableCondition.RowHeaderIndex + 1, k + 2].Style.Font.Bold = true;
                    }

                    ws.Cells[1,2, table.RowCount - 1,table.ColumnCount -1].AutoFitColumns();
                    ws.Cells.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
                    ws.Cells.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                  
                    ColWidth = ExcelHelper.PixelWidthToExcel(30);
                    ws.Column(1).Width = ColWidth;
                    // add row headers
                    for (int k = 0; k < table.ColumnCount; k++)
                    {
                        if (table.ColumnHidden[k])
                            continue;
                        ws.Row(k + 2).Height = ExcelHelper.PixelHeightToExcel(Height);
                    }

                    //Save and close the package.
                    p.Save();
                }
              
                
            }
            catch (Exception e)
            {

                return new Tuple<bool, string>(false, $"Fatal error: {e.Message} / {e.StackTrace}");
            }
          

            return new Tuple<bool, string>(true, "No error.");
        }

        /// <summary>
        /// Pumps message to the UI thread from another thread.
        /// </summary>
        /// <param name="message">Message.</param>
        void SendMessageToUI(string message)
        {
          
            window.Dispatcher.Invoke(() => {
                this.Message = message;
            });
        }
        void DoSomethingInMainThread(Action action)
        {

            window.Dispatcher.Invoke(() => {
                action();
            });
        }

        Task<Tuple<bool, string>> ProcessTableAsync(BomTableAnnotation bomTable, TableAnnotation table, TableBoundryCondition tableCondition, CancellationTokenSource source)
        {
            return Task<Tuple<bool, string>>.Run(() => {

                AddInContext.SOLIDWORKS.CommandInProgress = true;
var ret = ProcessTable(bomTable, table, tableCondition, source);
                AddInContext.SOLIDWORKS.CommandInProgress = false;
                return ret;
            });
        }

        bool CanExecuteStart()
        {
            if (IsIdle == true)
                if (string.IsNullOrWhiteSpace(SaveLocation) == false)
                    return true;

            return false;
        }
        #endregion 
    }

    #region helper classes/structs
    public class PictureHelper : System.Windows.Forms.AxHost
    {

        public PictureHelper()
            : base("56174C86-1546-4778-8EE6-B6AC606875E7")
        {

        }

        public static Image Convert(object objIDispImage)
        {
            Image objPicture = GetPictureFromIPicture(objIDispImage);
            return objPicture;
        }

    }
    #region excel help methods
  public static class ExcelHelper
    {
          public static  double PixelWidthToExcel(int pixels)
    {
        var tempWidth = pixels * 0.14099;
        var correction = (tempWidth / 100) * -1.30;

        return tempWidth - correction;
    }

    public static double PixelHeightToExcel(int pixels)
    {
        return pixels * 0.75;
    }
    }
    #endregion
    struct TableBoundryCondition
    {
        public swTableHeaderPosition_e HeaderPosition { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }

        public int RowHeaderIndex { get; set; }
    }
    #endregion
}