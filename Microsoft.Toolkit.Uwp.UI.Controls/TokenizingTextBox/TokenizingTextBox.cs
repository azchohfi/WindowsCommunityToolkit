﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Deferred;
using Microsoft.Toolkit.Uwp.Extensions;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Microsoft.Toolkit.Uwp.UI.Controls
{
    /// <summary>
    /// A text input control that auto-suggests and displays token items.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1124:Do not use regions", Justification = "Organization")]
    [TemplatePart(Name = PART_NormalState, Type = typeof(VisualState))]
    [TemplatePart(Name = PART_PointerOverState, Type = typeof(VisualState))]
    [TemplatePart(Name = PART_FocusedState, Type = typeof(VisualState))]
    [TemplatePart(Name = PART_UnfocusedState, Type = typeof(VisualState))]
    public partial class TokenizingTextBox : ListViewBase
    {
        internal const string PART_NormalState = "Normal";
        internal const string PART_PointerOverState = "PointerOver";
        internal const string PART_FocusedState = "Focused";
        internal const string PART_UnfocusedState = "Unfocused";

        /// <summary>
        /// Gets a value indicating whether the shift key is currently in a pressed state
        /// </summary>
        internal static bool IsShiftPressed => CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

        /// <summary>
        /// Gets a value indicating whether the control key is currently in a pressed state
        /// </summary>
        internal bool IsControlPressed => CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

        internal bool PauseTokenClearOnFocus { get; set; }

        internal bool IsClearingForClick { get; set; }

        private InterspersedObservableCollection _innerItemsSource;
        private PretokenStringContainer _currentTextEdit; // Don't update this directly outside of initialization, use UpdateCurrentTextEdit Method - in future see https://github.com/dotnet/csharplang/issues/140#issuecomment-625012514
        private PretokenStringContainer _lastTextEdit;

        /// <summary>
        /// Initializes a new instance of the <see cref="TokenizingTextBox"/> class.
        /// </summary>
        public TokenizingTextBox()
        {
            // Setup our base state of our collection
            _innerItemsSource = new InterspersedObservableCollection(new ObservableCollection<object>()); // TODO: Test this still will let us bind to ItemsSource in XAML?
            _currentTextEdit = _lastTextEdit = new PretokenStringContainer();
            _innerItemsSource.Insert(_innerItemsSource.Count, _currentTextEdit);
            ItemsSource = _innerItemsSource;
            //// TODO: Consolidate with callback below for ItemsSourceProperty changed?

            DefaultStyleKey = typeof(TokenizingTextBox);

            // TODO: Do we want to support ItemsSource better? Need to investigate how that works with adding...
            RegisterPropertyChangedCallback(ItemsSourceProperty, ItemsSource_PropertyChanged);
            PreviewKeyDown += TokenizingTextBox_PreviewKeyDown;
            PreviewKeyUp += TokenizingTextBox_PreviewKeyUp;
            CharacterReceived += TokenizingTextBox_CharacterReceived;
            ItemClick += TokenizingTextBox_ItemClick;

            PointerMoved += TokenizingTextBox_PointerMoved;
        }

        private void ItemsSource_PropertyChanged(DependencyObject sender, DependencyProperty dp)
        {
            // If we're given a different ItemsSource, we need to wrap that collection in our helper class.
            if (ItemsSource != null && ItemsSource.GetType() != typeof(InterspersedObservableCollection))
            {
                _innerItemsSource = new InterspersedObservableCollection(ItemsSource);
                _currentTextEdit = _lastTextEdit = new PretokenStringContainer();
                _innerItemsSource.Insert(_innerItemsSource.Count, _currentTextEdit);
                ItemsSource = _innerItemsSource;
            }
        }

        private void TokenizingTextBox_ItemClick(object sender, ItemClickEventArgs e)
        {
            // If the user taps an item in the list, make sure to clear any text selection as required
            // Note, token selection is cleared by the listview default behaviour
            if (!IsControlPressed)
            {
                // Set class state flag to prevent click item being immediately deselected
                IsClearingForClick = true;
                ClearAllTextSelections(null);
            }
        }

        private void TokenizingTextBox_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
        }

        private void TokenizingTextBox_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    {
                        // Clear any selection and place the focus back into the text box
                        DeselectAllTokensAndText();
                        FocusPrimaryAutoSuggestBox();
                        break;
                    }
            }
        }

        /// <summary>
        /// Set the focus to the last item in the collection
        /// </summary>
        private void FocusPrimaryAutoSuggestBox()
        {
            if (Items?.Count > 0)
            {
                (ContainerFromIndex(Items.Count - 1) as TokenizingTextBoxItem).Focus(FocusState.Programmatic);
            }
        }

        private async void TokenizingTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Global handlers on control regardless if focused on item or in textbox.
            switch (e.Key)
            {
                case VirtualKey.C:
                    if (IsControlPressed)
                    {
                        CopySelectedToClipboard();
                        e.Handled = true;
                    }

                    break;

                case VirtualKey.X:
                    if (IsControlPressed)
                    {
                        CopySelectedToClipboard();

                        // now clear all selected tokens and text, or all if none are selected
                        await RemoveAllSelectedTokens();
                    }

                    break;

                // For moving between tokens
                case Windows.System.VirtualKey.Left:
                    e.Handled = MoveFocusAndSelection(MoveDirection.Previous);
                    break;

                case Windows.System.VirtualKey.Right:
                    e.Handled = MoveFocusAndSelection(MoveDirection.Next);
                    break;

                case VirtualKey.A:
                    // modify the select-all behaviour to ensure the text in the edit box gets selected.
                    if (IsControlPressed)
                    {
                        this.SelectAllTokensAndText();
                        e.Handled = true;
                    }

                    break;
            }
        }

        /// <inheritdoc/>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var selectAllMenuItem = new MenuFlyoutItem
            {
                Text = "WindowsCommunityToolkit_TokenizingTextBox_MenuFlyout_SelectAll".GetLocalized("Microsoft.Toolkit.Uwp.UI.Controls/Resources")
            };
            selectAllMenuItem.Click += (s, e) => this.SelectAllTokensAndText();
            var menuFlyout = new MenuFlyout();
            menuFlyout.Items.Add(selectAllMenuItem);
            if (ControlHelpers.IsXamlRootAvailable && XamlRoot != null)
            {
                menuFlyout.XamlRoot = XamlRoot;
            }

            ContextFlyout = menuFlyout;
        }

        internal void RaiseQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            QuerySubmitted?.Invoke(sender, args);
        }

        internal void RaiseSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            SuggestionChosen?.Invoke(sender, args);
        }

        internal void RaiseTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            TextChanged?.Invoke(sender, args);
        }

        private async void TokenizingTextBox_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
        {
            // TODO: check to see if the character came from one of the tokens, and its not a control character
            var container = ContainerFromItem(_currentTextEdit) as TokenizingTextBoxItem;

            if (container != null && !(FocusManager.GetFocusedElement().Equals(container._autoSuggestTextBox) || char.IsControl(args.Character)))
            {
                if (SelectedItems.Count > 0)
                {
                    var index = _innerItemsSource.IndexOf(SelectedItems.First());

                    await RemoveAllSelectedTokens();

                    // Wait for removal of old items
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        // If we're before the last textbox and it's empty, redirect focus to that one instead
                        if (index == _innerItemsSource.Count - 1 && string.IsNullOrWhiteSpace(_lastTextEdit.Text))
                        {
                            var lastContainer = ContainerFromItem(_lastTextEdit) as TokenizingTextBoxItem;

                            lastContainer.UseCharacterAsUser = true; // Make sure we trigger a refresh of suggested items.

                            _lastTextEdit.Text = string.Empty + args.Character;

                            UpdateCurrentTextEdit(_lastTextEdit);

                            lastContainer._autoSuggestTextBox.SelectionStart = 1; // Set position to after our new character inserted

                            lastContainer._autoSuggestTextBox.Focus(FocusState.Keyboard);
                        }
                        else
                        {
                            //// Otherwise, create a new textbox for this text.

                            UpdateCurrentTextEdit(new PretokenStringContainer((string.Empty + args.Character).Trim())); // Trim so that 'space' isn't inserted and can be used to insert a new box.

                            _innerItemsSource.Insert(index, _currentTextEdit);

                            // Need to wait for containerization
                            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                var newContainer = ContainerFromIndex(index) as TokenizingTextBoxItem; // Should be our last text box

                                newContainer.UseCharacterAsUser = true; // Make sure we trigger a refresh of suggested items.

                                void WaitForLoad(object s, RoutedEventArgs eargs)
                                {
                                    if (newContainer._autoSuggestTextBox != null)
                                    {
                                        newContainer._autoSuggestTextBox.SelectionStart = 1; // Set position to after our new character inserted

                                        newContainer._autoSuggestTextBox.Focus(FocusState.Keyboard);
                                    }

                                    newContainer.Loaded -= WaitForLoad;
                                }

                                newContainer.AutoSuggestTextBoxLoaded += WaitForLoad;
                            });
                        }
                    });
                }
                else
                {
                    // TODO: It looks like we're setting selection and focus together on items? Not sure if that's what we want...
                    // If that's the case, don't think this code will ever be called?

                    //// TODO: Behavior question: if no items selected (just focus) does it just go to our last active textbox?
                    //// Community voted that typing in the end box made sense

                    if (_innerItemsSource[_innerItemsSource.Count - 1] is PretokenStringContainer textToken)
                    {
                        var last = ContainerFromIndex(Items.Count - 1) as TokenizingTextBoxItem; // Should be our last text box
                        var position = last._autoSuggestTextBox.SelectionStart;
                        textToken.Text = last._autoSuggestTextBox.Text.Substring(0, position) + args.Character +
                                         last._autoSuggestTextBox.Text.Substring(position);

                        last._autoSuggestTextBox.SelectionStart = position + 1; // Set position to after our new character inserted

                        last._autoSuggestTextBox.Focus(FocusState.Keyboard);
                    }
                }
            }
        }

        #region ItemsControl Container Methods

        /// <inheritdoc/>
        protected override DependencyObject GetContainerForItemOverride() => new TokenizingTextBoxItem();

        /// <inheritdoc/>
        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is TokenizingTextBoxItem;
        }

        /// <inheritdoc/>
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);

            var tokenitem = element as TokenizingTextBoxItem;

            tokenitem.Owner = this;

            tokenitem.ContentTemplateSelector = TokenItemTemplateSelector;
            tokenitem.ContentTemplate = TokenItemTemplate;

            tokenitem.ClearClicked -= TokenizingTextBoxItem_ClearClicked;
            tokenitem.ClearClicked += TokenizingTextBoxItem_ClearClicked;

            tokenitem.ClearAllAction -= TokenizingTextBoxItem_ClearAllAction;
            tokenitem.ClearAllAction += TokenizingTextBoxItem_ClearAllAction;

            tokenitem.GotFocus -= TokenizingTextBoxItem_GotFocus;
            tokenitem.GotFocus += TokenizingTextBoxItem_GotFocus;

            tokenitem.LostFocus -= TokenizingTextBoxItem_LostFocus;
            tokenitem.LostFocus += TokenizingTextBoxItem_LostFocus;

            var menuFlyout = new MenuFlyout();

            var removeMenuItem = new MenuFlyoutItem
            {
                Text = "WindowsCommunityToolkit_TokenizingTextBoxItem_MenuFlyout_Remove".GetLocalized("Microsoft.Toolkit.Uwp.UI.Controls/Resources")
            };
            removeMenuItem.Click += (s, e) => TokenizingTextBoxItem_ClearClicked(tokenitem, null);

            menuFlyout.Items.Add(removeMenuItem);
            if (ControlHelpers.IsXamlRootAvailable && XamlRoot != null)
            {
                menuFlyout.XamlRoot = XamlRoot;
            }

            var selectAllMenuItem = new MenuFlyoutItem
            {
                Text = "WindowsCommunityToolkit_TokenizingTextBox_MenuFlyout_SelectAll".GetLocalized("Microsoft.Toolkit.Uwp.UI.Controls/Resources")
            };
            selectAllMenuItem.Click += (s, e) => this.SelectAllTokensAndText();

            menuFlyout.Items.Add(selectAllMenuItem);

            tokenitem.ContextFlyout = menuFlyout;
        }
        #endregion

        private void TokenizingTextBoxItem_GotFocus(object sender, RoutedEventArgs e)
        {
            // Keep track of our currently focused textbox
            if (sender is TokenizingTextBoxItem ttbi && ttbi.Content is PretokenStringContainer text)
            {
                UpdateCurrentTextEdit(text);
            }
        }

        private void TokenizingTextBoxItem_LostFocus(object sender, RoutedEventArgs e)
        {
            // Keep track of our currently focused textbox
            if (sender is TokenizingTextBoxItem ttbi && ttbi.Content is PretokenStringContainer text &&
                string.IsNullOrWhiteSpace(text.Text) && text != _lastTextEdit)
            {
                // We're leaving an inner textbox that's blank, so we'll remove it
                _innerItemsSource.Remove(text);

                UpdateCurrentTextEdit(_lastTextEdit);
            }
        }

        /// <summary>
        /// Adds the specified data item as a new token to the collection, will raise the <see cref="TokenItemAdding"/> event asynchronously still for confirmation.
        /// </summary>
        /// <remarks>
        /// The <see cref="TokenizingTextBox"/> will automatically handle adding items for you, or you can add items to your backing <see cref="ItemsControl.ItemsSource"/> collection. This method is provide for other cases where you may need to drive inserting a new token item to where the user is currently inserting text between tokens.
        /// </remarks>
        /// <param name="data">Item to add as a token.</param>
        /// <param name="atEnd">Flag to indicate if the item should be inserted in the last used textbox (inserted) or placed at end of the token list.</param>
        public void AddTokenItem(object data, bool atEnd = false)
        {
            _ = AddTokenAsync(data, atEnd);
        }

        internal async Task AddTokenAsync(object data, bool? atEnd = null)
        {
            if (data is string str && TokenItemAdding != null)
            {
                var tiaea = new TokenItemAddingEventArgs(str);
                await TokenItemAdding.InvokeAsync(this, tiaea);

                if (tiaea.Cancel)
                {
                    return;
                }

                if (tiaea.Item != null)
                {
                    data = tiaea.Item; // Transformed by event implementor
                }
            }

            // If we've been typing in the last box, just add this to the end of our collection
            if (atEnd == true || _currentTextEdit == _lastTextEdit)
            {
                _innerItemsSource.InsertAt(_innerItemsSource.Count - 1, data);
            }
            else
            {
                // Otherwise, we'll insert before our current box
                var edit = _currentTextEdit;
                var index = _innerItemsSource.IndexOf(edit);

                // Insert our new data item at the location of our textbox
                _innerItemsSource.InsertAt(index, data);

                // Remove our textbox
                _innerItemsSource.Remove(edit);
            }

            // Focus back to our end box as Outlook does.
            var last = ContainerFromItem(_lastTextEdit) as TokenizingTextBoxItem;
            last._autoSuggestTextBox.Focus(FocusState.Keyboard);

            TokenItemAdded?.Invoke(this, data);
        }

        private void UpdateCurrentTextEdit(PretokenStringContainer edit)
        {
            _currentTextEdit = edit;

            Text = edit.Text; // Update our text property.
        }

        private async Task RemoveToken(TokenizingTextBoxItem item)
        {
            var data = ItemFromContainer(item);

            if (TokenItemRemoving != null)
            {
                var tirea = new TokenItemRemovingEventArgs(data, item);
                await TokenItemRemoving.InvokeAsync(this, tirea);

                if (tirea.Cancel)
                {
                    return;
                }
            }

            _innerItemsSource.Remove(data);

            TokenItemRemoved?.Invoke(this, data);
        }

        /// <summary>
        /// Returns the string representation of each token item, concatenated and delimeted.
        /// </summary>
        /// <returns>Untokenized text string</returns>
        public string GetUntokenizedText(string tokenDelimiter = ", ")
        {
            var tokenStrings = new List<string>();
            foreach (var item in Items)
            {
                tokenStrings.Add(item.ToString());
            }

            return string.Join(tokenDelimiter, tokenStrings);
        }
    }
}
