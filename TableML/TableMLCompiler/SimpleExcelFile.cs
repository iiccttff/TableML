﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace TableML.Compiler
{
    /// <summary>
    /// 对NPOI Excel封装, 支持xls, xlsx 和 tsv
    /// excel的行数从0开始，列数从1开始
    /// 带有头部、声明、注释
    /// </summary>
    public class SimpleExcelFile : ITableSourceFile
    {
        public Dictionary<string, int> ColName2Index { get; set; }
        public Dictionary<int, string> Index2ColName { get; set; }
        public Dictionary<string, string> ColName2Statement { get; set; } //  string,or something
        public Dictionary<string, string> ColName2Comment { get; set; } // string comment
       
        public string ExcelFileName { get; set; }

        #region excel文件的行和列定义

        //NOTE by zhaoqingqing 根据特殊的Excel格式定制
        /// <summary>
        /// Header, Statement, Comment, at lease 3 rows
        /// 预留行数
        /// </summary>
        public int PreserverRowCount
        {
            get { return ExcelConfig.IsKSFrameworkFormat ? 3 : 12; }
        }

        /// <summary>
        /// 从指定列开始读,默认是0
        /// </summary>
        public int StartColumnIdx
        {
            get
            {
                return ExcelConfig.IsKSFrameworkFormat ? 0 : 1;
            }
        }
		
        /// <summary>
        /// 字段名在第几行
        /// </summary>
        public int headRowIdx
        {
            get
            {
                return ExcelConfig.IsKSFrameworkFormat ? 0 : 5;
            }
        }
		
        /// <summary>
        /// 数据类型在第几行(比如：string,int,float)
        /// </summary>
        public int typeRowIdx
        {
            get
            {
                return ExcelConfig.IsKSFrameworkFormat ? 1 : 4;
            }
        }
		
        /// <summary>
        /// 注释在第几行
        /// </summary>
        public int commentRowIdx
        {
            get
            {
                return ExcelConfig.IsKSFrameworkFormat ? 2 : 15;
            }
        }
		
        /// <summary>
        /// 详细注释在第几行(ksframework无此行)
        /// </summary>
        public int commentDetailRowIdx
        {
            get
            {
                return ExcelConfig.IsKSFrameworkFormat ? 2 : 14;
            }
        }
		
        #endregion
        private string Path;
        private IWorkbook Workbook;
        private ISheet Worksheet;
        public bool IsLoadSuccess = true;
        private int _columnCount;
        private int sheetCount;
        public int SheetCount { get { return sheetCount; } private set { sheetCount = value; } }

        public SimpleExcelFile(string excelPath, int index=0)
        {
            Path = excelPath;
            ColName2Index = new Dictionary<string, int>();
            Index2ColName = new Dictionary<int, string>();
            ColName2Statement = new Dictionary<string, string>();
            ColName2Comment = new Dictionary<string, string>();
            ExcelFileName = System.IO.Path.GetFileName(excelPath);
            ParseExcel(excelPath, index);
        }

        /// <summary>
        /// Parse Excel file to data grid
        /// </summary>
        /// <param name="filePath"></param>
        private void ParseExcel(string filePath, int index)
        {
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) // no isolation
            {
                try
                {
                    Workbook = WorkbookFactory.Create(file);
                }
                catch (Exception e)
                {
                    //                    throw new Exception(string.Format("无法打开Excel: {0}, 可能原因：正在打开？或是Office2007格式（尝试另存为）？ {1}", filePath, e.Message));
                    ConsoleHelper.Error(string.Format("无法打开Excel: {0}, 可能原因：正在打开？或是Office2007格式（尝试另存为）？ {1}", filePath, e.Message));
                    IsLoadSuccess = false;
                    return;
                }
            }

            if (Workbook == null)
            {
                //                    throw new Exception(filePath + " Null Workbook");
                ConsoleHelper.Error(filePath + " Null Workbook");
                return;
            }
            SheetCount = Workbook.NumberOfSheets;
            //for (int idx = 0; idx < sheetCount; idx++)
            {
                ParseSheet(filePath, index);
            }

        }

        /// <summary>
        /// 检查excel是否符合输出规范
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool CheckRule(string filePath, int sheetIdx)
        {
            if (Workbook == null)
            {
                //                    throw new Exception(filePath + " Null Workbook");
                ConsoleHelper.Error(filePath + " Null Workbook");
                return false;
            }
            Worksheet = Workbook.GetSheetAt(sheetIdx);
            if (Worksheet == null)
            {
                //                    throw new Exception(filePath + " Null Worksheet");
                ConsoleHelper.Error(string.Format("{0} ,sheetIdx:{1} is Null Worksheet",filePath ,sheetIdx));
                return false;
            }
            
            var sheetRowCount = GetWorksheetCount();
            if (sheetRowCount < PreserverRowCount)
            {
                //                    throw new Exception(string.Format("{0} ,sheetIdx:{1} At lease {2} rows of this excel", filePath,sheetIdx, PreserverRowCount));
                ConsoleHelper.Error(string.Format("{0} ,sheetIdx:{1} At lease {2} rows of this excel", filePath,sheetIdx, PreserverRowCount));
                return false;

            }
            var row = Worksheet.GetRow(1);
            if (row == null || row.Cells.Count < 2)
            {
                //                throw new Exception(filePath + "第二行至少需要3列");
                ConsoleHelper.Error("{0}, sheetIdx:{1} ,name:{2} 的第二行至少需要3列",filePath ,sheetIdx, Worksheet.SheetName);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 解析excel的sheet
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="sheetIdx"></param>
        public void ParseSheet(string filePath, int sheetIdx)
        {
            if (CheckRule(filePath, sheetIdx) == false) { return; }
            if (Worksheet == null) return;
            /**表头结构如下所示：
            *   Id  Name    CDTime
            *   int string int
            *   编号 名称 CD时间
            */

            //NOTE 从第0行开始读
            // 列头名(字段名) 
            var headerRow = Worksheet.GetRow(headRowIdx);
            //npoi的列数是从0开始,而excel的列数是从1开始

            _columnCount = headerRow.LastCellNum;
            // 列总数保存
            int columnCount = GetColumnCount();

            //NOTE by qingqing-zhao 列名：从指定的列开始读取
            int emptyColumn = 0;
            for (int columnIndex = StartColumnIdx; columnIndex <= columnCount; columnIndex++)
            {
                var cell = headerRow.GetCell(columnIndex);
                var headerName = cell != null ? cell.ToString().Trim() : ""; // trim!
                var realIdx = columnIndex - StartColumnIdx;
                if (string.IsNullOrEmpty(headerName))
                {
                    //NOTE 如果列名是空，当作注释处理，因为老表可能已经写了#1，#2
                    emptyColumn += 1;
                    headerName = string.Concat("#Comment#", emptyColumn);
                }
                ColName2Index[headerName] = realIdx;
                Index2ColName[realIdx] = headerName;
            }
            // 表头声明(数据类型)
            var statementRow = Worksheet.GetRow(typeRowIdx);
            for (int columnIndex = StartColumnIdx; columnIndex <= columnCount; columnIndex++)
            {
                var realIdx = columnIndex - StartColumnIdx;
                if (Index2ColName.ContainsKey(realIdx) == false)
                {
                    continue;
                }
                var colName = Index2ColName[realIdx];
                var statementCell = statementRow.GetCell(columnIndex);
                var statementString = statementCell != null ? statementCell.ToString() : "";
                ColName2Statement[colName] = statementString;
            }
            
            IRow commentRow = null;
            IRow commentRowDetail = null;
          
            if (ExcelConfig.IsKSFrameworkFormat)
            {
                commentRow = Worksheet.GetRow(commentRowIdx);
            }
            else
            {
                // 表头注释(字段注释) 我们有两行注释
                 commentRow = Worksheet.GetRow(commentRowIdx);
                 commentRowDetail = Worksheet.GetRow(commentDetailRowIdx);
            }

            for (int columnIndex = StartColumnIdx; columnIndex <= columnCount; columnIndex++)
            {
                var realIdx = columnIndex - StartColumnIdx;
                if (Index2ColName.ContainsKey(realIdx) == false)
                {
                    continue;
                }
                var colName = Index2ColName[realIdx];
                var commentCell = commentRow.GetCell(columnIndex);
                ICell commentCellDetail = null;
                if (commentRowDetail != null)
                {
                    commentCellDetail = commentRowDetail.GetCell(columnIndex);
                }
                string commentString = string.Empty;
                if (commentCell != null)
                {
                    commentString += string.Concat(GetCellString(commentCell), "\n");
                }
                if (commentCellDetail != null)
                {
                    commentString += GetCellString(commentCellDetail);
                }
                //fix 注释包含\r\n
                if (commentString.Contains("\n"))
                {
                    commentString = CombieLine(commentString, "\n");
                }
                else if (commentString.Contains("\r\n"))
                {
                    commentString = CombieLine(commentString, "\r\n");
                }
                ColName2Comment[colName] = commentString;
            }
        }

        public string CombieLine(string commentString, string lineStr)
        {
            if (commentString.Contains(lineStr))
            {
                var comments = commentString.Split(new string[] { lineStr }, StringSplitOptions.None);
                StringBuilder sb = new StringBuilder();
                sb.Append(comments[0]);
                for (int idx = 1; idx < comments.Length; idx++)
                {
                    if (string.IsNullOrEmpty(comments[idx])) continue;
                    sb.Append(string.Concat("\r\n", "       ///  ", comments[idx]));
                }
                commentString = sb.ToString();
            }
            return commentString;
        }

        /// <summary>
        /// 统一接口：获取单元格内容
        /// </summary>
        /// <param name="cell"></param>
        /// <returns></returns>
        public static string GetCellString(ICell cell)
        {
            if (cell == null) return "";
            string result = string.Empty;
            switch (cell.CellType)
            {
                case CellType.Unknown:
                    result = cell.StringCellValue;
                    break;
                case CellType.Numeric:
                    result = cell.NumericCellValue.ToString(CultureInfo.InvariantCulture);
                    break;
                case CellType.String:
                    result = cell.StringCellValue;
                    break;
                case CellType.Formula:
                    //NOTE 单元格为公式，分类型
                    switch (cell.CachedFormulaResultType)
                    {
                        //已测试的公式:SUM,& 
                        case CellType.Numeric:
                            result = cell.NumericCellValue.ToString();
                            break;
                        case CellType.String:
                            result = cell.StringCellValue;
                            break;
                    }
                    break;
                case CellType.Blank:
                    result = "";
                    break;
                case CellType.Boolean:
                    result = cell.BooleanCellValue ? "1" : "0";
                    break;
                case CellType.Error:
                    result = cell.ErrorCellValue.ToString();
                    break;
                default:
                    result = "未知类型";
                    break;
            }
            return result;
        }

        /// <summary>
        /// 是否存在列名
        /// </summary>
        /// <param name="columnName"></param>
        /// <returns></returns>
        public bool HasColumn(string columnName)
        {
            return ColName2Index.ContainsKey(columnName);
        }

        /// <summary>
        /// 清除行内容
        /// </summary>
        /// <param name="row"></param>
        public void ClearRow(int row)
        {
            if (Worksheet != null)
            {
                var theRow = Worksheet.GetRow(row);
                Worksheet.RemoveRow(theRow);
            }
        }

        public float GetFloat(string columnName, int row)
        {
            return float.Parse(GetString(columnName, row));
        }

        public int GetInt(string columnName, int row)
        {
            return int.Parse(GetString(columnName, row));
        }

        /// <summary>
        /// </summary>
        /// <param name="columnName"></param>
        /// <param name="dataRow">无计算表头的数据行数</param>
        /// <returns></returns>
        public string GetString(string columnName, int dataRow)
        {
            if (Worksheet == null) return null;
            dataRow += PreserverRowCount;

            var theRow = Worksheet.GetRow(dataRow);
            if (theRow == null)
                theRow = Worksheet.CreateRow(dataRow);

            var colIndex = ColName2Index[columnName] + StartColumnIdx;
            var cell = theRow.GetCell(colIndex);
            if (cell == null)
                cell = theRow.CreateCell(colIndex);

            return GetCellString(cell);
        }

        /// <summary>
        /// 不带预留头的数据总行数
        /// </summary>
        /// <returns></returns>
        public int GetRowsCount()
        {
            return GetWorksheetCount() - PreserverRowCount;
        }

        /// <summary>
        /// 工作表的总行数
        /// </summary>
        /// <returns></returns>
        private int GetWorksheetCount()
        {
            return Worksheet == null ? 0 : Worksheet.LastRowNum + 1;
        }

        private ICellStyle GreyCellStyleCache;

        public void SetRowGrey(int row)
        {
            if (Worksheet == null) { return; }
            var theRow = Worksheet.GetRow(row);
            foreach (var cell in theRow.Cells)
            {
                if (GreyCellStyleCache == null)
                {
                    var newStyle = Workbook.CreateCellStyle();
                    newStyle.CloneStyleFrom(cell.CellStyle);
                    //newStyle.FillBackgroundColor = colorIndex;
                    newStyle.FillPattern = FillPattern.Diamonds;
                    GreyCellStyleCache = newStyle;
                }

                cell.CellStyle = GreyCellStyleCache;
            }
        }

        public void SetRow(string columnName, int row, string value)
        {
            if (!ColName2Index.ContainsKey(columnName))
            {
                //                throw new Exception(string.Format("No Column: {0} of File: {1}", columnName, Path));
                ConsoleHelper.Error(string.Format("No Column: {0} of File: {1}", columnName, Path));
                return;
            }
            if (Worksheet == null) return;
            var theRow = Worksheet.GetRow(row);
            if (theRow == null)
                theRow = Worksheet.CreateRow(row);
            var cell = theRow.GetCell(ColName2Index[columnName]);
            if (cell == null)
                cell = theRow.CreateCell(ColName2Index[columnName]);

            if (value.Length > (1 << 14)) // if too long
            {
                value = value.Substring(0, 1 << 14);
            }
            cell.SetCellValue(value);
        }

        public void Save(string toPath)
        {
            /*for (var loopRow = Worksheet.FirstRowNum; loopRow <= Worksheet.LastRowNum; loopRow++)
        {
            var row = Worksheet.GetRow(loopRow);
            bool emptyRow = true;
            foreach (var cell in row.Cells)
            {
                if (!string.IsNullOrEmpty(cell.ToString()))
                    emptyRow = false;
            }
            if (emptyRow)
                Worksheet.RemoveRow(row);
        }*/
            //try
            {
                using (var memStream = new MemoryStream())
                {
                    Workbook.Write(memStream);
                    memStream.Flush();
                    memStream.Position = 0;

                    using (var fileStream = new FileStream(toPath, FileMode.Create, FileAccess.Write))
                    {
                        var data = memStream.ToArray();
                        fileStream.Write(data, 0, data.Length);
                        fileStream.Flush();
                    }
                }
            }
            //catch (Exception e)
            //{
            //    CDebug.LogError(e.Message);
            //    CDebug.LogError("是否打开了Excel表？");
            //}
        }

        public void Save()
        {
            Save(Path);
        }

        /// <summary>
        /// 获取列总数
        /// </summary>
        /// <returns></returns>
        public int GetColumnCount()
        {
            return _columnCount - StartColumnIdx;
        }



        /// <summary>
        /// 读表中的字段获取输出文件名
        /// 做好约定输出tml文件名在指定的单元格，不用遍历整表让解析更快
        /// </summary>
        /// <returns></returns>
        public static string GetOutFileName(string filePath, int sheetIdx = 0)
        {
            if (ExcelConfig.IsKSFrameworkFormat)
            {
                return System.IO.Path.GetFileNameWithoutExtension(filePath);
            }
            IWorkbook workbook;
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) // no isolation
            {
                try
                {
                    workbook = WorkbookFactory.Create(file);
                }
                catch (Exception e)
                {
                    //                    throw new Exception(string.Format("无法打开Excel: {0}, 可能原因：正在打开？或是Office2007格式（尝试另存为）？ {1}", filePath, e.Message));
                    ConsoleHelper.Error(string.Format("无法打开Excel: {0}, 可能原因：正在打开？或是Office2007格式（尝试另存为）？ {1}", filePath, e.Message));
                    return "";
                }
            }
            var worksheet = workbook.GetSheetAt(sheetIdx);
            if (worksheet == null)
            {
                //                throw new Exception(filePath + "Null Worksheet");
                ConsoleHelper.Error(filePath + "Null Worksheet");
                return "";
            }
            var row = worksheet.GetRow(1);
            if (row == null || row.Cells.Count < 2)
            {
                //                throw new Exception(filePath + "第二行至少需要3列");
                ConsoleHelper.Error(filePath + " -> " + worksheet.SheetName + "第二行至少需要3列");
                return "";
            }

            var outFileName = GetCellString(row.Cells[2]);
            var shouldOutput = GetCellString(row.Cells[1]);
            if ((shouldOutput.ToInt32_()) == 0)
            {
                return "";
            }
            return outFileName;
        }
    }
}