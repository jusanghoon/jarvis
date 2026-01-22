using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using javis.ViewModels;

namespace javis.UI.Controls
{
    public partial class MainAiWidget : UserControl
    {
        public MainAiWidget()
        {
            InitializeComponent();
            this.Loaded += MainAiWidget_Loaded;
        }

        private void MainAiWidget_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainAiWidgetViewModel vm)
            {
                // 1. 초기 기동 알림: 심장을 한 번 번쩍이게 함
                SoloHeart?.Flash();

                // 2. ViewModel 프로퍼티 변경 감지하여 심장 효과 동기화
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainAiWidgetViewModel.IsSoloThinkingStarting))
                    {
                        if (vm.IsSoloThinkingStarting)
                        {
                            // 사유가 시작되는 순간 심장 박동 강조
                            SoloHeart?.Flash();
                        }
                    }
                };
            }
        }
    }
}
