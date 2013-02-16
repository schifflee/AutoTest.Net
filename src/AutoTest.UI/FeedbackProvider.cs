using System;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using AutoTest.Messages;
using System.IO;
using AutoTest.UI.TextFormatters;

namespace AutoTest.UI
{
	public class FeedbackProvider
	{
		private readonly SynchronizationContext _syncContext;
        private TestDetailsForm _testDetails;
        private readonly object _messagLock = new object();
        private bool _isRunning;
        private bool _progressUpdatedExternally;
        private ImageStates _lastInternalState = ImageStates.None;
        private readonly string _progressPicture;
        private bool _iconVisible = true;

        private Action<bool> _setIconVisibility = (visible) => {};
        private Action _prepareForFocus = () => {};
        private Action _clearList = () => {};
        private Action<string> _clearBuilds = (project) => {};
        private Func<bool> _isInFocus = () => false;
        private Action<string,ImageStates,string> _updatePicture = (picture, state, information) => {};
        private Action<string,string,bool> _printMessage = (message,color,normal) => {};
        private Action _storeSelected () => {};
        private Action _restoreSelected () => {};
        private Action<Func<CacheTestMessage,bool> _removeTest = ((t) => false) => {};
        private Action<Func<CacheBuildMessage,bool> _removeBuildItem = ((t) => false) => {};
        private Action<string,string,string,object> _addItem = (type, message, color, tag) => {};
        private Action<string> _setSummary = (m) => {};

        private bool _showErrors = true;
        private bool _showWarnings = true;
        private bool _showFailing = true;
        private bool _showIgnored = true;

        public event EventHandler<GoToReferenceArgs> GoToReference;
        public event EventHandler<GoToTypeArgs> GoToType;
        public event EventHandler<DebugTestArgs> DebugTest;
        public event EventHandler CancelRun;

        public bool CanGoToTypes { get; set; }
        public bool CanDebug { get; set; }
        public int ListViewWidthOffset { get; set; }
        public bool ShowRunInformation { get; set; }


        public bool ShowIcon
        {
            get
            {
                return _iconVisible;
            }
            set
            {
                _iconVisible = value;
                _setIconVisibility(value);
            }
        }

        public FeedbackProvider()
        {
            _syncContext = AsyncOperationManager.SynchronizationContext;
            CanDebug = false;
            CanGoToTypes = false;
            ShowRunInformation = true;
            organizeListItemBehaviors();
            _progressPicture = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "progress.gif");
        }

        // Move stuff around on the form
        public void ReOrganize()
        {
            _syncContext.Post(message =>
            {
                organizeListItemBehaviors();
            }, null);
        }

        public void PrepareForFocus()
        {
            _syncContext.Post(x =>
            {
                _prepareForFocus();
                organizeListItemBehaviors();
            }, null);
        }

        public void ClearList()
        {
            _syncContext.Post(x =>
            {
                _clearList();
            }, null);
        }

        public void ClearBuilds()
        {
            _syncContext.Post(x =>
            {
                _clearBuilds(null);
            }, null);
        }

        public void ClearBuilds(string proj)
        {
            _syncContext.Post(x =>
            {
                _clearBuilds(proj);
            }, proj);
        }

        public bool IsInFocus()
        {
            return _isInFocus();
        }

        public void SetVisibilityConfiguration(bool showErrors, bool showWarnings, bool showFailingTests, bool showIgnoredTests)
        {
            _showErrors = showErrors;
            _showWarnings = showWarnings;
            _showFailing = showFailingTests;
            _showIgnored = showIgnoredTests;
            _syncContext.Post(x =>
            {
                ClearList();
            }, null);
        }

        public bool isTheSameTestAs(CacheTestMessage original, CacheTestMessage item)
        {
            return 
                original.Assembly.Equals(item.Assembly) &&
                original.Test.Runner.Equals(item.Test.Runner) &&
                original.Test.Name.Equals(item.Test.Name) &&
                original.Test.DisplayName.Equals(item.Test.DisplayName);
        }

        public void ConsumeMessage(object msg)
        {
            _syncContext.Post(message =>
            {
                lock (_messagLock)
                {
                    try
                    {
                        if (message.GetType() == typeof(CacheMessages))
                            handle((CacheMessages)message);
                        else if (message.GetType() == typeof(LiveTestStatusMessage))
                            handle((LiveTestStatusMessage)message);
                        else if (message.GetType() == typeof(RunStartedMessage))
                            runStarted("Detected file changes...");
                        else if (message.GetType() == typeof(RunFinishedMessage))
                            runFinished((RunFinishedMessage)message);
                        else if (message.GetType() == typeof(RunInformationMessage))
                            runInformationMessage((RunInformationMessage)message);
			            else if (message.GetType() == typeof(BuildRunMessage)) {
				            if (((BuildRunMessage)message).Results.Errors.Length == 0)
					            ClearBuilds(((BuildRunMessage)message).Results.Project); // Make sure no errors remain in log
                        }
                    }
                    catch
                    {
                    }

                }
            }, msg);
        }

        private void handle(CacheMessages cacheMessages)
        {
            Handle(cacheMessages);
        }

        private void handle(LiveTestStatusMessage liveTestStatusMessage)
        {
            Handle(liveTestStatusMessage);
        }

        private void runStarted(string x)
        {
            if (!ShowRunInformation)
                x = "processing changes...";
            printMessage(new RunMessages(RunMessageType.Normal, x.ToString()));
            generateSummary(null);
            organizeListItemBehaviors();
            clearRunnerTypeAnyItems();
            setProgress(ImageStates.Progress, "processing changes...", false, null, true);
            _isRunning = true;
            organizeListItemBehaviors();
        }

        public void SetProgress(bool on, string information, string picture)
        {
            _progressUpdatedExternally = on;
            var state = _lastInternalState;
            if (on)
                state = ImageStates.Progress;
            setProgress(state, information, true, picture);
        }

        public void SetProgress(bool on, string information, ImageStates imageState)
        {
            _progressUpdatedExternally = on;
            setProgress(imageState, information, true, null);
        }

        private void setProgress(ImageStates state, string information, bool external, string picture)
        {
            setProgress(state, information, external, picture, false);
        }

        private void setProgress(ImageStates state, string information, bool external, string picture, bool force)
        {
            if (!force && _progressUpdatedExternally && !external)
                return;
            if (picture == null)
                picture = _progressPicture;

            _updatePicture(picture, state, information);

            if (!external)
                _lastInternalState = state;
        }

        private void runFinished(RunFinishedMessage x)
        {
            if (((RunFinishedMessage)x).Report.Aborted)
            {
                if (ShowRunInformation)
                {
                    var i = getRunFinishedInfo((RunFinishedMessage)x);
                    var runType = i.Succeeded ? RunMessageType.Succeeded : RunMessageType.Failed;
                    setProgress(runType == RunMessageType.Succeeded ? ImageStates.Green : ImageStates.Red, "", false, null);
                    printMessage(new RunMessages(runType, i.Text));
                    generateSummary(i.Report);
                }
            }
            else
            {
                var i = getRunFinishedInfo((RunFinishedMessage)x);
                var runType = i.Succeeded ? RunMessageType.Succeeded : RunMessageType.Failed;
                setProgress(runType == RunMessageType.Succeeded ? ImageStates.Green : ImageStates.Red, "", false, null);
                printMessage(new RunMessages(runType, i.Text));
                generateSummary(i.Report);
            }
            _isRunning = false;
            organizeListItemBehaviors();
        }

        private RunFinishedInfo getRunFinishedInfo(RunFinishedMessage message)
        {
            var report = message.Report;
            var text = string.Format(
                        "Ran {0} build(s) ({1} succeeded, {2} failed) and {3} test(s) ({4} passed, {5} failed, {6} ignored)",
                        report.NumberOfProjectsBuilt,
                        report.NumberOfBuildsSucceeded,
                        report.NumberOfBuildsFailed,
                        report.NumberOfTestsRan,
                        report.NumberOfTestsPassed,
                        report.NumberOfTestsFailed,
                        report.NumberOfTestsIgnored);
            var succeeded = !(report.NumberOfBuildsFailed > 0 || report.NumberOfTestsFailed > 0);
            return new RunFinishedInfo(text, succeeded, report);
        }

        private void runInformationMessage(RunInformationMessage x)
        {
            if (!_isRunning)
                return;
            var text = "";
            var message = (RunInformationMessage)x;
            switch (message.Type)
            {
                case InformationType.Build:
                    if (ShowRunInformation)
                        text = string.Format("building {0}", Path.GetFileName(message.Project));
                    break;
                case InformationType.TestRun:
                    text = "testing...";
                    break;
                case InformationType.PreProcessing:
                    if (ShowRunInformation)
                        text = "locating affected tests";
                    break;
            }
            if (text != "") {
                setProgress(ImageStates.Progress, text.ToString(), false, null);
                printMessage(new RunMessages(RunMessageType.Normal, text.ToString()));
            }
        }

        public void PrintMessage(RunMessages message)
        {
            _syncContext.Post(x => printMessage((RunMessages)x), message);
        }

        private void printMessage(RunMessages message)
        {
            var msg = message;
            var normal = true;
            var color = "black";
            label1.Text = msg.Message;

            if (msg.Type == RunMessageType.Succeeded)
            {
                color = "green";
                normal = false;
            }
            if (msg.Type == RunMessageType.Failed)
            {
                color = "red";
                normal = false;
            }

            _printMessage(msg.Message, color, normal);
        }

        private new void Handle(CacheMessages cache)
        {
            _storeSelected();
            
            removeItems(cache);
            if (_showErrors)
            {
                foreach (var error in cache.ErrorsToAdd)
                    addFeedbackItem("Build error", formatBuildResult(error), Color.Red, error, selected, 0);
            }

            if (_showFailing)
            {
                var position = getFirstItemPosition(new[] { "Test failed", "Build warning", "Test ignored" });
                foreach (var failed in cache.FailedToAdd)
                    addFeedbackItem("Test failed", formatTestResult(failed), Color.Red, failed, selected, position);
            }

            if (_showWarnings)
            {
                var position = getFirstItemPosition(new[] { "Build warning", "Test ignored" });
                foreach (var warning in cache.WarningsToAdd)
                    addFeedbackItem("Build warning", formatBuildResult(warning), Color.Black, warning, selected, position);
            }

            if (_showIgnored)
            {
                var position = getFirstItemPosition(new[] { "Test ignored" });
                foreach (var ignored in cache.IgnoredToAdd)
                    addFeedbackItem("Test ignored", formatTestResult(ignored), Color.Black, ignored, selected, position);
            }
            
            _restoreSelected();
        }

        private new void Handle(LiveTestStatusMessage liveStatus)
        {
            if (!_isRunning)
                return;

            _storeSelected();

            var ofCount = liveStatus.TotalNumberOfTests > 0 ? string.Format(" of {0}", liveStatus.TotalNumberOfTests) : "";
            var testName = liveStatus.CurrentTest;
            if (testName.Trim().Length > 0)
                testName += " in ";
            printMessage(new RunMessages(RunMessageType.Normal, string.Format("testing {3}{0} ({1}{2} tests completed)", Path.GetFileNameWithoutExtension(liveStatus.CurrentAssembly), liveStatus.TestsCompleted, ofCount, testName)));

            if (_showFailing)
            {
                foreach (var test in liveStatus.FailedButNowPassingTests)
                {
                    var testItem = new CacheTestMessage(test.Assembly, test.Test);
                    _removeTest((t) => isTheSameTestAs(testItem, t));
                }

                foreach (var test in liveStatus.FailedTests)
                {
                    var testItem = new CacheTestMessage(test.Assembly, test.Test);
                    _removeTest((t) => isTheSameTestAs(testItem, t));
                    addFeedbackItem("Test failed", formatTestResult(testItem), Color.Red, testItem, selected, index);
                }
            }

            _restoreSelected();
        }

        private void clearRunnerTypeAnyItems()
        {
            var toRemove = new List<ListViewItem>();
            _removeTest((t) => t.Test.Runner == TestRunner.Any);
        }

        private void removeItems(CacheMessages cache)
        {
            foreach (var item in cache.ErrorsToRemove)
                _removeBuildItem((itm) => itm.Equals(item));
            foreach (var item in cache.WarningsToRemove)
                _removeBuildItem((itm) => itm.Equals(item));

            foreach (var item in cache.TestsToRemove)
                _removeTest((t) => {
                        return
                            t.Assembly.Equals(item.Assembly) &&
                            t.Test.Runner.Equals(item.Test.Runner) &&
                            t.Test.Name.Equals(item.Test.Name);
                    });
        }

        private void addFeedbackItem(string type, string message, Color colour, object tag)
        {
            if (testExists(tag))
                return;
            _addItem(type, message, colour.ToString().ToLower(), tag);
        }

        private bool isWindows() {
            return 
                Environment.OSVersion.Platform == PlatformID.Win32S ||
                Environment.OSVersion.Platform == PlatformID.Win32NT ||
                Environment.OSVersion.Platform == PlatformID.Win32Windows ||
                Environment.OSVersion.Platform == PlatformID.WinCE ||
                Environment.OSVersion.Platform == PlatformID.Xbox;
        }

        private bool testExists(object tag)
        {
            if (tag.GetType() != typeof(CacheTestMessage))
                return false;
            var test = (CacheTestMessage)tag;
            return _exists((item) => item.GetType() == typeof(CacheTestMessage) && isTheSameTestAs(test, item));
        }

        private string formatBuildResult(CacheBuildMessage item)
        {
            return string.Format("{0}, {1}", item.BuildItem.ErrorMessage, item.BuildItem.File);
        }

        private string formatTestResult(CacheTestMessage item)
        {
            return string.Format("{0} -> ({1}) {2}", item.Test.Status, item.Test.Runner.ToString(), item.Test.DisplayName);
        }

        private void organizeListItemBehaviors()
        {
            using (var handler = new ListItemBehaviourHandler(this))
            {
                handler.Organize(collection, _isRunning);
            }
        }

        private void goToBuildItemReference(CacheBuildMessage buildItem)
        {
            goToReference(buildItem.BuildItem.File, buildItem.BuildItem.LineNumber, buildItem.BuildItem.LinePosition);
        }

        private void goToTestItemReference(CacheTestMessage testItem)
        {
            if (testItem.Test.StackTrace.Length > 0)
            {
				var exactLine = getMatchingStackLine(testItem);
				if (exactLine != null) {
					goToReference(exactLine.File, exactLine.LineNumber, 0);
					return;
				}
				
                if (CanGoToTypes)
                    if (goToType(testItem.Assembly, testItem.Test.Name))
                        return;
            }
        }

		private IStackLine getMatchingStackLine(CacheTestMessage testItem)
		{
			foreach (var line in testItem.Test.StackTrace) {
				if (line.Method.Equals(testItem.Test.Name))
					return line;
			}
            var lastWithLine = testItem.Test.StackTrace.LastOrDefault(x => x.LineNumber > 0);
            if (lastWithLine != null)
                return lastWithLine;

			return null;
		}

        private void goToReference(string file, int lineNumber, int column)
        {
            if (GoToReference != null)
                GoToReference(this, new GoToReferenceArgs(new CodePosition(file, lineNumber, column)));
        }

        private bool goToType(string assembly, string typename)
        {
            var type = typename.Replace("+", ".");
            var args = new GoToTypeArgs(assembly, type);
            if (GoToType != null)
                GoToType(this, args);
            return args.Handled;
        }

        private void generateSummary(RunReport report)
        {
            if (report == null)
            {
                _toolTipProvider.RemoveAll();
                return;
            }

            var builder = new SummaryBuilder(report);
            _setSummary(builder.Build());
        }
    }

    public class GoToReferenceArgs : EventArgs
    {
        public CodePosition Position { get; private set; }
        public GoToReferenceArgs(CodePosition position) { Position = position; }
    }

    public class GoToTypeArgs : EventArgs
    {
        public bool Handled = true;
        public string Assembly { get; private set; }
        public string TypeName { get; private set; }
        public GoToTypeArgs(string assembly, string typename) { Assembly = assembly; TypeName = typename; }
    }

    public class DebugTestArgs : EventArgs
    {
        public CacheTestMessage Test { get; private set; }
        public DebugTestArgs(CacheTestMessage test) { Test = test; }
    }
}