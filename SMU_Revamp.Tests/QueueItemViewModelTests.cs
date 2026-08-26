using SMU_Revamp.Models;
using SMU_Revamp.ViewModels;
using Xunit;

namespace SMU_Revamp.Tests
{
    public class QueueItemViewModelTests
    {
        [Fact]
        public void PresetItem_DisplayName_ShowsNameAndPlan()
        {
            var preset = new MeasurementPreset { Name = "Forming", PlanName = "Memristor Sweep" };
            var item = new QueueItemViewModel(QueueItemType.Preset, preset);

            Assert.Equal(QueueItemType.Preset, item.Type);
            Assert.Equal("Forming", item.DisplayName);
        }

        [Fact]
        public void PresetItem_WithRepetitions_ShowsMultiplier()
        {
            var preset = new MeasurementPreset { Name = "Forming", PlanName = "U-Sweep" };
            var item = new QueueItemViewModel(QueueItemType.Preset, preset) { Repetitions = 3 };

            Assert.Equal("Forming ×3", item.DisplayName);
        }

        [Theory]
        [InlineData(60, "1 min")]
        [InlineData(300, "5 min")]
        [InlineData(90, "90 s")]
        [InlineData(0.5, "0.5 s")]
        public void PauseItem_DisplayName_FormatsDuration(double seconds, string expectedSuffix)
        {
            var item = new QueueItemViewModel(QueueItemType.Pause) { PauseSeconds = seconds };

            Assert.Equal($"Pause · {expectedSuffix}", item.DisplayName);
            Assert.Equal(expectedSuffix, QueueItemViewModel.FormatDuration(seconds));
        }

        [Fact]
        public void Repetitions_IsClampedToAtLeastOne()
        {
            var item = new QueueItemViewModel(QueueItemType.Preset, new MeasurementPreset());

            item.Repetitions = -5;
            Assert.Equal(1, item.Repetitions);

            item.Repetitions = 4;
            Assert.Equal(4, item.Repetitions);
        }

        [Fact]
        public void PauseSeconds_IsClampedToNonNegative()
        {
            var item = new QueueItemViewModel(QueueItemType.Pause);

            item.PauseSeconds = -10;
            Assert.Equal(0, item.PauseSeconds);

            item.PauseSeconds = 45;
            Assert.Equal(45, item.PauseSeconds);
        }

        [Fact]
        public void NewItems_StartPendingAndNotRunning()
        {
            var presetItem = new QueueItemViewModel(QueueItemType.Preset, new MeasurementPreset());
            var pauseItem = new QueueItemViewModel(QueueItemType.Pause);

            foreach (var item in new[] { presetItem, pauseItem })
            {
                Assert.Equal("Pending", item.Status);
                Assert.False(item.IsRunning);
                Assert.False(item.IsDone);
                Assert.False(item.IsFailed);
            }
        }

        [Fact]
        public void StatusTransitions_AreObservable()
        {
            var item = new QueueItemViewModel(QueueItemType.Preset, new MeasurementPreset());
            string? lastStatus = null;
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(QueueItemViewModel.Status))
                {
                    lastStatus = item.Status;
                }
            };

            item.Status = "Running";
            Assert.Equal("Running", lastStatus);

            item.IsDone = true;
            item.Status = "Done";
            Assert.Equal("Done", lastStatus);
        }
    }
}
