using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Listen Together screen. The room logic still lives in MainPage.ListenTogether.cs, which reads the named
    /// elements directly (x:FieldModifier="internal") and handles the events below; each event forwards the
    /// original sender so handlers that use sender/DataContext (kick member, approve/reject join) keep working.
    /// </summary>
    public sealed partial class ListenTogetherPanel : UserControl
    {
        public event RoutedEventHandler CloseListenTogetherViewClick;
        public event RoutedEventHandler LtApproveJoinClick;
        public event RoutedEventHandler LtAutoApproveSuggestionsToggleToggled;
        public event RoutedEventHandler LtAutoApproveToggleToggled;
        public event RoutedEventHandler LtCancelJoinClick;
        public event RoutedEventHandler LtCloseSettingsClick;
        public event TappedEventHandler LtCodeBoxesTapped;
        public event RoutedEventHandler LtConnectClick;
        public event RoutedEventHandler LtCopyCodeClick;
        public event RoutedEventHandler LtCreateRoomClick;
        public event RoutedEventHandler LtDismissErrorClick;
        public event TextChangedEventHandler LtHiddenCodeBoxTextChanged;
        public event RoutedEventHandler LtJoinRoomClick;
        public event RoutedEventHandler LtKickMemberClick;
        public event RoutedEventHandler LtLeaveRoomClick;
        public event RoutedEventHandler LtOpenSettingsClick;
        public event RoutedEventHandler LtRejectJoinClick;
        public event RoutedEventHandler LtSaveNicknameClick;
        public event RoutedEventHandler LtSettingsApplyServerClick;
        public event TappedEventHandler LtSettingsCardTapped;
        public event RoutedEventHandler LtSettingsResetServerClick;
        public event RoutedEventHandler LtShareCodeClick;
        public event RoutedEventHandler LtSyncVolumeToggleToggled;

        public ListenTogetherPanel()
        {
            this.InitializeComponent();
        }

        private void CloseListenTogetherView_Click(object sender, RoutedEventArgs e) { if (CloseListenTogetherViewClick != null) CloseListenTogetherViewClick(sender, e); }
        private void LtApproveJoin_Click(object sender, RoutedEventArgs e) { if (LtApproveJoinClick != null) LtApproveJoinClick(sender, e); }
        private void LtAutoApproveSuggestionsToggle_Toggled(object sender, RoutedEventArgs e) { if (LtAutoApproveSuggestionsToggleToggled != null) LtAutoApproveSuggestionsToggleToggled(sender, e); }
        private void LtAutoApproveToggle_Toggled(object sender, RoutedEventArgs e) { if (LtAutoApproveToggleToggled != null) LtAutoApproveToggleToggled(sender, e); }
        private void LtCancelJoin_Click(object sender, RoutedEventArgs e) { if (LtCancelJoinClick != null) LtCancelJoinClick(sender, e); }
        private void LtCloseSettings_Click(object sender, RoutedEventArgs e) { if (LtCloseSettingsClick != null) LtCloseSettingsClick(sender, e); }
        private void LtCodeBoxes_Tapped(object sender, TappedRoutedEventArgs e) { if (LtCodeBoxesTapped != null) LtCodeBoxesTapped(sender, e); }
        private void LtConnect_Click(object sender, RoutedEventArgs e) { if (LtConnectClick != null) LtConnectClick(sender, e); }
        private void LtCopyCode_Click(object sender, RoutedEventArgs e) { if (LtCopyCodeClick != null) LtCopyCodeClick(sender, e); }
        private void LtCreateRoom_Click(object sender, RoutedEventArgs e) { if (LtCreateRoomClick != null) LtCreateRoomClick(sender, e); }
        private void LtDismissError_Click(object sender, RoutedEventArgs e) { if (LtDismissErrorClick != null) LtDismissErrorClick(sender, e); }
        private void LtHiddenCodeBox_TextChanged(object sender, TextChangedEventArgs e) { if (LtHiddenCodeBoxTextChanged != null) LtHiddenCodeBoxTextChanged(sender, e); }
        private void LtJoinRoom_Click(object sender, RoutedEventArgs e) { if (LtJoinRoomClick != null) LtJoinRoomClick(sender, e); }
        private void LtKickMember_Click(object sender, RoutedEventArgs e) { if (LtKickMemberClick != null) LtKickMemberClick(sender, e); }
        private void LtLeaveRoom_Click(object sender, RoutedEventArgs e) { if (LtLeaveRoomClick != null) LtLeaveRoomClick(sender, e); }
        private void LtOpenSettings_Click(object sender, RoutedEventArgs e) { if (LtOpenSettingsClick != null) LtOpenSettingsClick(sender, e); }
        private void LtRejectJoin_Click(object sender, RoutedEventArgs e) { if (LtRejectJoinClick != null) LtRejectJoinClick(sender, e); }
        private void LtSaveNickname_Click(object sender, RoutedEventArgs e) { if (LtSaveNicknameClick != null) LtSaveNicknameClick(sender, e); }
        private void LtSettingsApplyServer_Click(object sender, RoutedEventArgs e) { if (LtSettingsApplyServerClick != null) LtSettingsApplyServerClick(sender, e); }
        private void LtSettingsCard_Tapped(object sender, TappedRoutedEventArgs e) { if (LtSettingsCardTapped != null) LtSettingsCardTapped(sender, e); }
        private void LtSettingsResetServer_Click(object sender, RoutedEventArgs e) { if (LtSettingsResetServerClick != null) LtSettingsResetServerClick(sender, e); }
        private void LtShareCode_Click(object sender, RoutedEventArgs e) { if (LtShareCodeClick != null) LtShareCodeClick(sender, e); }
        private void LtSyncVolumeToggle_Toggled(object sender, RoutedEventArgs e) { if (LtSyncVolumeToggleToggled != null) LtSyncVolumeToggleToggled(sender, e); }
    }
}
