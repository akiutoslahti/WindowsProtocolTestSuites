﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommandLine;
using Microsoft.Protocols.TestManager.Kernel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.Protocols.TestManager.CLI
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA1016 Mark assemblies with AssemblyVersionAttribute")]
    class Program
    {
        static void Main(string[] args)
        {
            CheckParameters(args);
            var parser = new Parser(cfg =>
            {
                cfg.CaseInsensitiveEnumValues = true;
                cfg.HelpWriter = Console.Error;
            });
            parser.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => Run(opts))
                .WithNotParsed<Options>(errs => HandleArgumentError(errs));
        }

        static void Run(Options options)
        {
            Logger.EnableDebugging = options.EnableDebugging;
            UpdateTestCaseListCallback capabilitiesReportHandler = null;
            var capabilitiesResultsTree = 
                new Dictionary<string, Dictionary<string, TestCase>>();

            try
            {
                Program p = new Program();
                p.Init();

                var resultFileDir = Path.GetDirectoryName(options.ReportFile);
                if (!string.IsNullOrEmpty(resultFileDir) && !Directory.Exists(resultFileDir))
                {
                    throw new ArgumentException(String.Format(StringResources.InvalidTestResultDir, resultFileDir));
                }

                if (!Path.IsPathRooted(options.Profile))
                {
                    options.Profile = Utility.RelativePath2AbsolutePath(options.Profile);
                }

                if (!Path.IsPathRooted(options.TestSuite))
                {
                    options.TestSuite = Utility.RelativePath2AbsolutePath(options.TestSuite);
                }

                Logger.AddLog(LogLevel.Information, options.ToString());

                var config = p.ParseConfigItems(options.Configuration);

                p.LoadTestSuite(options.Profile, options.TestSuite, config);

                List<TestCase> testCases;

                if(!options.IsUsingCapabilitiesFiltering())
                {
                    if (string.IsNullOrWhiteSpace(options.FilterExpression))
                    {
                        testCases = p.GetTestCases(options.SelectedOnly);
                    }
                    else
                    {
                        testCases = p.GetTestCases(options.FilterExpression);
                    }
                }
                else
                {
                    Logger.AddLog(LogLevel.Information, $"Loading test cases using the following capabilities specification file: {options.CapabilitiesSpecification}.");
                    
                    var reader =
                        CapabilitiesConfigReader.Parse(new FileInfo(options.CapabilitiesSpecification));
                    var filters = options.CapabilitiesFilterExpression?.Split(',');

                    Logger.AddLog(LogLevel.Information, $"Using {filters.Length} capabilities specification filters.");

                    var testNames = reader.GetTestCases(filters);
                    testCases = p.GetTestCases(testNames);

                    // Subscribe to the test runs and update the results by group/category
                    // if capabilities filtering is in use.
                    capabilitiesReportHandler = (group, runningCase) =>
                    {
                        var testFullName = runningCase.FullName;

                        var testCaseCategories = reader.GetCategoriesFor(testFullName)
                                                        .Select(f => f.ToLowerInvariant());

                        foreach (var c in testCaseCategories)
                        {
                            if (!capabilitiesResultsTree.ContainsKey(c))
                            {
                                capabilitiesResultsTree.Add(c, new Dictionary<string, TestCase>());
                            }

                            if (!capabilitiesResultsTree[c].ContainsKey(testFullName))
                            {
                                capabilitiesResultsTree[c].Add(testFullName, runningCase);
                            }
                            else
                            {
                                capabilitiesResultsTree[c][testFullName] = runningCase;
                            }
                        }
                    };
                }

                Console.CancelKeyPress += (sender, args) =>
                {
                    Console.WriteLine("\nAborting test suite.");
                    p.AbortExecution();
                };

                p.RunTestSuite(testCases, capabilitiesReportHandler);

                if (options.ReportFile == null)
                {
                    p.PrintTestReport(options.Outcome);
                }
                else
                {
                    if (!Path.IsPathRooted(options.ReportFile))
                    {
                        options.ReportFile = Path.Combine(p.util.TestResultOutputFolder, options.ReportFile);
                    }
                    p.SaveTestReport(options.ReportFile, options.ReportFormat, options.Outcome);
                    Console.WriteLine(String.Format(StringResources.ReportFilePath, options.ReportFile));

                    if (options.IsUsingCapabilitiesFiltering())
                    {
                        var outputFolder = Path.GetDirectoryName(options.ReportFile);
                        p.SaveTestReportByCapabilities(outputFolder,
                            options.ReportFormat, options.Outcome, capabilitiesResultsTree);
                    }
                }

                Console.WriteLine(String.Format(StringResources.TestResultPath, Path.Combine(options.TestSuite, p.util.GetTestEngineResultPath())));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(StringResources.ErrorMessage);
                Console.Error.WriteLine(e.Message);
                Logger.AddLog(LogLevel.Error, e.Message);
                Environment.Exit(-1);
            }
        }

        static void HandleArgumentError(IEnumerable<Error> errors)
        {
            foreach (var error in errors)
            {
                Logger.AddLog(LogLevel.Error, error.ToString());
            }
            Environment.Exit(-1);
        }

        static void CheckParameters(string[] args)
        {
            var _param = new Dictionary<string, string>();

            // Parse parameters to dictionary
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null) continue;
                string key = null;
                string val = null;

                // If the beginning of this string instance matches the specified string '-', it should be key
                if (args[i].StartsWith('-'))
                {
                    key = args[i].Replace("-", string.Empty);

                    // Get the value from next
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        val = args[i + 1];
                        i++;
                    }
                }
                else
                {
                    val = args[i];
                }

                // Adjustment
                if (key == null)
                {
                    key = val;
                    val = null;
                }
                _param[key] = val;
            }

            if (_param.ContainsKey("outcome"))
            {
                var outcomeValue = _param["outcome"].Split(',');
                foreach (var e in outcomeValue)
                {
                    // if the value converts integer succeeded, it shows error. valid values are: pass, fail, inconclusive. 
                    if (int.TryParse(e, out _))
                    {
                        Console.Error.WriteLine(StringResources.ErrorMessage);
                        Console.WriteLine(StringResources.OutcomeErrorMessage);
                        Environment.Exit(-1);
                    }
                }
            }

            if (_param.ContainsKey("f") || _param.ContainsKey("format"))
            {
                var formatValue = _param.ContainsKey("f") ?
                    _param["f"] :
                    _param["format"];
                if (int.TryParse(formatValue, out _))
                {
                    Console.Error.WriteLine(StringResources.ErrorMessage);
                    Console.WriteLine(StringResources.FormatErrorMessage);
                    Environment.Exit(-1);
                }
            }

            if (_param.ContainsKey("cfile"))
            {
                if(!_param.ContainsKey("cfilter"))
                {
                    Console.Error.WriteLine(StringResources.ErrorMessage);
                    Console.WriteLine(StringResources.MissingCapabilitiesFilterMessage);
                    Environment.Exit(-1);
                }
            }
        }
        Utility util;
        List<TestSuiteInfo> testSuites;

        /// <summary>
        /// Initialize
        /// </summary>
        public void Init()
        {
            util = new Utility();
            testSuites = util.TestSuiteIntroduction.SelectMany(tsFamily => tsFamily).ToList();
        }

        private IDictionary<string, string> ParseConfigItems(IEnumerable<string> config)
        {
            // {property_name}={property_value}
            var exp = new Regex(@"^(?<property_name>[^=]+)=(?<property_value>.*)$");

            var result = config
                            .Select(s =>
                            {
                                var match = exp.Match(s);

                                if (!match.Success)
                                {
                                    throw new ArgumentException($"The configuration item format is invalid: \"{s}\". Expected format: {{property_name}}={{property_value}}.");
                                }

                                return match;
                            })
                            .ToDictionary(
                                match => match.Groups["property_name"].Value,
                                match => match.Groups["property_value"].Value
                            );

            return result;
        }

        /// <summary>
        /// Load test suite.
        /// </summary>
        /// <param name="filename">Filename of the profile</param>
        /// <param name="testSuiteFolder">Path of the specified test suite</param>
        /// <param name="config">Configuration items which will override values in profile.</param>
        public void LoadTestSuite(string filename, string testSuiteFolder, IDictionary<string, string> config)
        {
            Logger.AddLog(LogLevel.Information, "Load Test Suite");
            string testSuiteFolderBin = Path.Combine(testSuiteFolder, "Bin");
            TestSuiteInfo tsinfo;
            using (ProfileUtil profile = ProfileUtil.LoadProfile(filename))
            {
                tsinfo = testSuites.Find(ts => ts.TestSuiteName == profile.Info.TestSuiteName);
                if (tsinfo == null)
                {
                    throw new ArgumentException(String.Format(StringResources.UnknownTestSuiteMessage, profile.Info.TestSuiteName));
                }

                tsinfo.TestSuiteFolder = testSuiteFolder;
                tsinfo.TestSuiteVersion = LoadTestsuiteVersion(testSuiteFolderBin);
            }

            util.LoadTestSuiteConfig(tsinfo);
            util.LoadTestSuiteAssembly();

            string newProfile;
            if (util.TryUpgradeProfileSettings(filename, testSuiteFolderBin, out newProfile))
            {
                Console.WriteLine(String.Format(StringResources.PtmProfileUpgraded, newProfile));
                filename = newProfile;
            }
            util.LoadProfileSettings(filename, testSuiteFolderBin);

            util.UpdatePtfConfig(config);
        }

        /// <summary>
        /// Get test cases using profile
        /// </summary>
        /// <param name="selectedOnly">True to run only the test cases selected in the run page.</param>
        public List<TestCase> GetTestCases(bool selectedOnly)
        {
            List<TestCase> testCaseList = new List<TestCase>();
            foreach (TestCase testcase in util.GetSelectedCaseList())
            {
                if (!selectedOnly || testcase.IsChecked) testCaseList.Add(testcase);
            }
            return testCaseList;
        }

        /// <summary>
        /// Get test cases using filter expression.
        /// </summary>
        /// <param name="filterExpression">The filter expression.</param>
        /// <returns>The test case list.</returns>
        public List<TestCase> GetTestCases(string filterExpression)
        {
            var result = util.GetTestCasesByFilter(filterExpression);

            return result;
        }

        /// <summary>
        /// Get test cases using a capabilities file.
        /// </summary>
        /// <param name="capabilitiesFilePath">The path to the capabilities file.</param>
        /// <param name="capabilitiesFilter">Filter expression to use with the capabilities specification.</param>
        /// <returns>The test case list.</returns>
        public List<TestCase> GetTestCases(string[] testNames)
        {
            Logger.AddLog(LogLevel.Information, $"Found {testNames.Length} matching test name(s).");
            Logger.AddLog(LogLevel.Debug, $"The following matching test name(s) were found: {string.Join('|', testNames)}.");

            var testCases = util.GetTestSuite().TestCaseList
                                   .Where(t => testNames.Contains(t.FullName))
                                   .ToList();

            Logger.AddLog(LogLevel.Information, $"Found {testCases.Count} matching test case(s).");

            return testCases;
        }

        /// <summary>
        /// Get test cases using category parameter
        /// </summary>
        /// <param name="category">The specific category of test cases to run</param>
        public List<TestCase> GetTestCases(List<string> categories)
        {
            List<TestCase> testCaseList = new List<TestCase>();

            Filter filter = new Filter(categories, RuleType.Selector);
            foreach (TestCase testcase in util.GetTestSuite().TestCaseList)
            {
                if (filter.FilterTestCase(testcase.Category)) testCaseList.Add(testcase);
            }
            return testCaseList;
        }

        /// <summary>
        /// Run test suite
        /// </summary>
        /// <param name="testCases">The list of test cases to run</param>
        /// <param name="handlers">List of event handlers that can be used to handle status changes for test cases as they're being run.</param>
        public void RunTestSuite(List<TestCase> testCases,
            params UpdateTestCaseListCallback[] handlers)
        {
            Logger.AddLog(LogLevel.Information, "Run Test Suite");
            using (ProgressBar progress = new ProgressBar())
            {
                util.SetSelectedCaseList(testCases);

                util.InitializeTestEngine();

                int total = testCases.Count;
                int executed = 0;

                TestSuiteLogManager tsLogManager = util.GetTestSuiteLogManager();
                var caseSet = new HashSet<string>();
                tsLogManager.GroupByOutcome.UpdateTestCaseList += (group, runningcase) =>
                {
                    if (caseSet.Contains(runningcase.Name))
                    {
                        return;
                    }
                    caseSet.Add(runningcase.Name);
                    executed++;
                    progress.Update((double)executed / total, $"({executed}/{total}) Executing {runningcase.Name}");
                };

                foreach (var handler in handlers) 
                {
                    if (handler != null)
                    {
                        tsLogManager.GroupByOutcome.UpdateTestCaseList += handler;
                    }
                }

                progress.Update(0, "Loading test suite");
                util.SyncRunByCases(testCases);
            }

            Console.WriteLine();
            Console.WriteLine(StringResources.FinishRunningTips);
        }

        /// <summary>
        /// Abort test suite
        /// </summary>
        public void AbortExecution()
        {
            util.AbortExecution();
            Logger.AddLog(LogLevel.Information, "Execution is aborted");
        }

        /// <summary>
        /// Print plain test report to console.
        /// </summary>
        public void PrintTestReport(IEnumerable<Outcome> outcomes)
        {
            Logger.AddLog(LogLevel.Information, "Print Test Report");
            bool pass = outcomes.Contains(Outcome.Pass);
            bool fail = outcomes.Contains(Outcome.Fail);
            bool inconclusive = outcomes.Contains(Outcome.Inconclusive);

            var testcases = util.SelectTestCases(pass, fail, inconclusive, false);
            PlainReport report = new PlainReport(testcases);
            Console.Write(report.GetPlainReport());
        }

        /// <summary>
        /// Save test report to disk.
        /// </summary>
        public void SaveTestReport(string filename, ReportFormat format, IEnumerable<Outcome> outcomes)
        {
            Logger.AddLog(LogLevel.Information, "Save Test Report");

            bool pass = outcomes.Contains(Outcome.Pass);
            bool fail = outcomes.Contains(Outcome.Fail);
            bool inconclusive = outcomes.Contains(Outcome.Inconclusive);

            var testcases = util.SelectTestCases(pass, fail, inconclusive, false);

            TestReport report = TestReport.GetInstance(format.ToString(), testcases);
            if (report == null)
            {
                throw new Exception(String.Format(StringResources.UnknownReportFormat, format.ToString()));
            }

            report.ExportReport(filename);
        }


        /// <summary>
        /// Save test report to disk by capabilities category.
        /// </summary>
        /// <param name="outputFolder">Root folder to output the results to.</param>
        /// <param name="format">The format of the results.</param>
        /// <param name="outcomes">The outcomes to include in the reports.</param>
        /// <param name="capabilitiesResultsTree">A dictionary of capabilities categories and associated test cases.</param>
        /// <exception cref="InvalidOperationException">Thrown when an invalid report format is specified.</exception>
        public void SaveTestReportByCapabilities(string outputFolder,
            ReportFormat format, IEnumerable<Outcome> outcomes,
            Dictionary<string, Dictionary<string, TestCase>> capabilitiesResultsTree)
        {
            Logger.AddLog(LogLevel.Information, "Save Test Report By Capabilities");

            outputFolder = @$"{outputFolder}/capabilities_report";
            if(Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder);
            }

            Directory.CreateDirectory(outputFolder);

            bool pass = outcomes.Contains(Outcome.Pass);
            bool fail = outcomes.Contains(Outcome.Fail);
            bool inconclusive = outcomes.Contains(Outcome.Inconclusive);

            foreach (var identifier in capabilitiesResultsTree.Keys) 
            {
                var fileName = format switch
                {
                    ReportFormat.Json => $"{identifier}.json",
                    ReportFormat.XUnit => $"{identifier}.xml",
                    _ => $"{identifier}.txt"
                };

                var path = Path.Combine(outputFolder, fileName);

                var testCases = capabilitiesResultsTree[identifier].Select(t => t.Value).ToList();

                var report = TestReport.GetInstance(format.ToString(), testCases);
                if (report == null)
                {
                    throw new InvalidOperationException(String.Format(StringResources.UnknownReportFormat, format.ToString()));
                }

                report.ExportReport(path);
            }
        }

        /// <summary>
        /// load test suite version info
        /// </summary>
        /// <param name="testsuitepath">The path of test case</param>
        private string LoadTestsuiteVersion(string testsuitepath)
        {
            List<string> paths = new List<string>();
            var allVersions = new HashSet<string>();

            // Version info is saved in ".version" file.
            var versionPath = Directory.GetFiles(testsuitepath, ".version", SearchOption.TopDirectoryOnly);
            if (versionPath != null && versionPath.Length == 1)
            {
                var version = File.ReadAllLines(versionPath[0]);
                if (version != null)
                {
                    return version[0];
                }
            }

            throw new Exception(StringResources.InvalidTestSuiteVersion);
        }
    }
}
