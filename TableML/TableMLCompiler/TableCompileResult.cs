﻿using System;
using System.Collections.Generic;
namespace TableML.Compiler
{
    /// <summary>
    /// Invali Excel Exception
    /// </summary>
    public class InvalidExcelException : Exception
    {
        public InvalidExcelException(string msg)
            : base(msg)
        {
        }
    }

    /// <summary>
    /// 返回编译结果
    /// </summary>
    public class TableCompileResult
    {
        public string TabFileFullPath { get; set; }
        public string TabFileRelativePath { get; set; }
        /// <summary>
        /// column + type
        /// </summary>
        public List<TableColumnVars> FieldsInternal { get; set; } 

        public string PrimaryKey { get; set; }
        public ITableSourceFile ExcelFile { get; internal set; }
        public string TabFileNames { get { return ExcelFile.ExcelFileName; } }
        public TableCompileResult()
        {
            FieldsInternal = new List<TableColumnVars>();
        }

    }

}
