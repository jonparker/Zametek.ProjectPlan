﻿/*************************************************************************************
   
   Toolkit for WPF

   Copyright (C) 2007-2018 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at http://wpftoolkit.codeplex.com/license 

   For more features, controls, and fast professional support,
   pick up the Plus Edition at https://xceed.com/xceed-toolkit-plus-for-wpf/

   Stay informed: follow @datagrid on Twitter or Like http://facebook.com/datagrids

  ***********************************************************************************/

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using Xceed.Wpf.Toolkit.Core;

namespace Xceed.Wpf.Toolkit.Zoombox
{
    public sealed class ZoomboxViewStack : Collection<ZoomboxView>, IWeakEventListener
    {
        #region Constructors

        public ZoomboxViewStack(Zoombox zoombox)
        {
            _zoomboxRef = new WeakReference(zoombox);
        }

        #endregion

        #region SelectedView Property

        public ZoomboxView SelectedView
        {
            get
            {
                int currentIndex = this.Zoombox.ViewStackIndex;
                return (currentIndex < 0 || currentIndex > Count - 1) ? ZoomboxView.Empty : this[currentIndex];
            }
        }

        #endregion

        #region AreViewsFromSource Internal Property

        internal bool AreViewsFromSource
        {
            get
            {
                return _cacheBits[(int)CacheBits.AreViewsFromSource];
            }
            set
            {
                _cacheBits[(int)CacheBits.AreViewsFromSource] = value;
            }
        }

        #endregion

        #region Source Internal Property

        internal IEnumerable Source
        {
            get
            {
                return _source;
            }
        }

        // if the view stack is generated by items within the ViewStackSource collection
        // of the Zoombox, then we maintain a strong reference to the source
        private IEnumerable _source; //null

        #endregion

        #region IsChangeFromSource Private Property

        private bool IsChangeFromSource
        {
            get
            {
                return _cacheBits[(int)CacheBits.IsChangeFromSource];
            }
            set
            {
                _cacheBits[(int)CacheBits.IsChangeFromSource] = value;
            }
        }

        #endregion

        #region IsMovingViews Private Property

        private bool IsMovingViews
        {
            get
            {
                return _cacheBits[(int)CacheBits.IsMovingViews];
            }
            set
            {
                _cacheBits[(int)CacheBits.IsMovingViews] = value;
            }
        }

        #endregion

        #region IsResettingViews Private Property

        private bool IsResettingViews
        {
            get
            {
                return _cacheBits[(int)CacheBits.IsResettingViews];
            }
            set
            {
                _cacheBits[(int)CacheBits.IsResettingViews] = value;
            }
        }

        #endregion

        #region IsSettingInitialViewAfterClear Private Property

        private bool IsSettingInitialViewAfterClear
        {
            get
            {
                return _cacheBits[(int)CacheBits.IsSettingInitialViewAfterClear];
            }
            set
            {
                _cacheBits[(int)CacheBits.IsSettingInitialViewAfterClear] = value;
            }
        }

        #endregion

        #region Zoombox Private Property

        private Zoombox Zoombox
        {
            get
            {
                return _zoomboxRef.Target as Zoombox;
            }
        }

        // maintain a weak reference to the Zoombox that owns the stack
        private WeakReference _zoomboxRef;

        #endregion

        internal void ClearViewStackSource()
        {
            if (this.AreViewsFromSource)
            {
                this.AreViewsFromSource = false;
                this.MonitorSource(false);
                _source = null;
                using (new SourceAccess(this))
                {
                    this.Clear();
                }
                this.Zoombox.CoerceValue(Zoombox.ViewStackModeProperty);
            }
        }

        internal void PushView(ZoomboxView view)
        {
            // clear the forward stack
            int currentIndex = this.Zoombox.ViewStackIndex;
            while (this.Count - 1 > currentIndex)
            {
                this.RemoveAt(Count - 1);
            }
            this.Add(view);
        }

        internal void SetViewStackSource(IEnumerable source)
        {
            if (_source != source)
            {
                this.MonitorSource(false);
                _source = source;
                this.MonitorSource(true);
                this.AreViewsFromSource = true;
                this.Zoombox.CoerceValue(Zoombox.ViewStackModeProperty);
                this.ResetViews();
            }
        }

        protected override void ClearItems()
        {
            this.VerifyStackModification();

            bool currentDeleted = (this.Zoombox.CurrentViewIndex >= 0);
            base.ClearItems();
            this.Zoombox.SetViewStackCount(Count);

            // if resetting the views due to a change in the view source collection, just return
            if (this.IsResettingViews)
                return;

            if (this.Zoombox.EffectiveViewStackMode == ZoomboxViewStackMode.Auto && this.Zoombox.CurrentView != ZoomboxView.Empty)
            {
                this.IsSettingInitialViewAfterClear = true;
                try
                {
                    this.Add(this.Zoombox.CurrentView);
                }
                finally
                {
                    this.IsSettingInitialViewAfterClear = false;
                }
                this.Zoombox.ViewStackIndex = 0;
                if (currentDeleted)
                {
                    this.Zoombox.SetCurrentViewIndex(0);
                }
            }
            else
            {
                this.Zoombox.ViewStackIndex = -1;
                this.Zoombox.SetCurrentViewIndex(-1);
            }
        }

        protected override void InsertItem(int index, ZoomboxView view)
        {
            this.VerifyStackModification();

            if (this.Zoombox.HasArrangedContentPresenter
                && this.Zoombox.ViewStackIndex >= index
                && !this.IsSettingInitialViewAfterClear
                && !this.IsResettingViews
                && !this.IsMovingViews)
            {
                bool oldUpdatingView = this.Zoombox.IsUpdatingView;
                this.Zoombox.IsUpdatingView = true;
                try
                {
                    this.Zoombox.ViewStackIndex++;
                    if (this.Zoombox.CurrentViewIndex != -1)
                    {
                        this.Zoombox.SetCurrentViewIndex(this.Zoombox.CurrentViewIndex + 1);
                    }
                }
                finally
                {
                    this.Zoombox.IsUpdatingView = oldUpdatingView;
                }
            }

            base.InsertItem(index, view);
            this.Zoombox.SetViewStackCount(Count);
        }

        protected override void RemoveItem(int index)
        {
            this.VerifyStackModification();

            bool currentDeleted = (this.Zoombox.ViewStackIndex == index);
            if (!this.IsMovingViews)
            {
                // if an item below the current index was deleted 
                // (or if the last item is currently selected and it was deleted),
                // adjust the ViewStackIndex and CurrentViewIndex values
                if (this.Zoombox.HasArrangedContentPresenter
                    && (this.Zoombox.ViewStackIndex > index
                        || (currentDeleted && this.Zoombox.ViewStackIndex == this.Zoombox.ViewStack.Count - 1)))
                {
                    // if removing the last item, just clear the stack, which ensures the proper
                    // behavior based on the ViewStackMode
                    if (currentDeleted && this.Zoombox.ViewStack.Count == 1)
                    {
                        this.Clear();
                        return;
                    }

                    bool oldUpdatingView = this.Zoombox.IsUpdatingView;
                    this.Zoombox.IsUpdatingView = true;
                    try
                    {
                        this.Zoombox.ViewStackIndex--;
                        if (this.Zoombox.CurrentViewIndex != -1)
                        {
                            this.Zoombox.SetCurrentViewIndex(this.Zoombox.CurrentViewIndex - 1);
                        }
                    }
                    finally
                    {
                        this.Zoombox.IsUpdatingView = oldUpdatingView;
                    }
                }
            }

            base.RemoveItem(index);

            // if the current view was deleted, we may need to update the view index 
            // (unless a non-stack view is in effect)
            if (!this.IsMovingViews && currentDeleted && this.Zoombox.CurrentViewIndex != -1)
            {
                this.Zoombox.RefocusView();
            }

            this.Zoombox.SetViewStackCount(Count);
        }

        protected override void SetItem(int index, ZoomboxView view)
        {
            this.VerifyStackModification();

            base.SetItem(index, view);

            // if the set item is the current item, update the zoombox
            if (index == this.Zoombox.CurrentViewIndex)
            {
                this.Zoombox.RefocusView();
            }
        }

        private static ZoomboxView GetViewFromSourceItem(object item)
        {
            ZoomboxView view = (item is ZoomboxView) ? item as ZoomboxView : ZoomboxViewConverter.Converter.ConvertFrom(item) as ZoomboxView;
            if (view == null)
                throw new InvalidCastException(string.Format(ErrorMessages.GetMessage("UnableToConvertToZoomboxView"), item));

            return view;
        }

        private void InsertViews(int index, IList newItems)
        {
            using (new SourceAccess(this))
            {
                foreach (object item in newItems)
                {
                    ZoomboxView view = ZoomboxViewStack.GetViewFromSourceItem(item);
                    if (index >= this.Count)
                    {
                        this.Add(view);
                    }
                    else
                    {
                        this.Insert(index, view);
                    }
                    index++;
                }
            }
        }

        private void MonitorSource(bool monitor)
        {
            if (_source != null && (_source is INotifyCollectionChanged))
            {
                if (monitor)
                {
                    CollectionChangedEventManager.AddListener(_source as INotifyCollectionChanged, this);
                }
                else
                {
                    CollectionChangedEventManager.RemoveListener(_source as INotifyCollectionChanged, this);
                }
            }
        }

        private void MoveViews(int oldIndex, int newIndex, IList movedItems)
        {
            using (new SourceAccess(this))
            {
                int currentIndex = this.Zoombox.ViewStackIndex;
                int indexAfterMove = currentIndex;

                // adjust the current index, if it was affected by the move
                if (!((oldIndex < currentIndex && newIndex < currentIndex)
                    || (oldIndex > currentIndex && newIndex > currentIndex)))
                {
                    if (currentIndex >= oldIndex && currentIndex < oldIndex + movedItems.Count)
                    {
                        indexAfterMove += newIndex - oldIndex;
                    }
                    else if (currentIndex >= newIndex)
                    {
                        indexAfterMove += movedItems.Count;
                    }
                }

                this.IsMovingViews = true;
                try
                {
                    for (int i = 0; i < movedItems.Count; i++)
                    {
                        this.RemoveAt(oldIndex);
                    }
                    for (int i = 0; i < movedItems.Count; i++)
                    {
                        this.Insert(newIndex + i, ZoomboxViewStack.GetViewFromSourceItem(movedItems[i]));
                    }
                    if (indexAfterMove != currentIndex)
                    {
                        this.Zoombox.ViewStackIndex = indexAfterMove;
                        this.Zoombox.SetCurrentViewIndex(indexAfterMove);
                    }
                }
                finally
                {
                    this.IsMovingViews = false;
                }
            }
        }

        private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    this.InsertViews(e.NewStartingIndex, e.NewItems);
                    break;

                case NotifyCollectionChangedAction.Move:
                    this.MoveViews(e.OldStartingIndex, e.NewStartingIndex, e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    this.RemoveViews(e.OldStartingIndex, e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    this.ResetViews();
                    break;

                case NotifyCollectionChangedAction.Reset:
                    this.ResetViews();
                    break;
            }
        }

        private void ResetViews()
        {
            using (new SourceAccess(this))
            {
                int currentIndex = this.Zoombox.ViewStackIndex;
                this.IsResettingViews = true;
                try
                {
                    this.Clear();
                    foreach (object item in _source)
                    {
                        ZoomboxView view = ZoomboxViewStack.GetViewFromSourceItem(item);
                        this.Add(view);
                    }

                    currentIndex = Math.Min(Math.Max(0, currentIndex), this.Count - 1);

                    this.Zoombox.ViewStackIndex = currentIndex;
                    this.Zoombox.SetCurrentViewIndex(currentIndex);
                    this.Zoombox.RefocusView();
                }
                finally
                {
                    this.IsResettingViews = false;
                }
            }
        }

        private void RemoveViews(int index, IList removedItems)
        {
            using (new SourceAccess(this))
            {
                for (int i = 0; i < removedItems.Count; i++)
                {
                    this.RemoveAt(index);
                }
            }
        }

        private void VerifyStackModification()
        {
            if (this.AreViewsFromSource && !this.IsChangeFromSource)
                throw new InvalidOperationException(ErrorMessages.GetMessage("ViewStackCannotBeManipulatedNow"));
        }

        #region IWeakEventListener Members

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            if (managerType == typeof(CollectionChangedEventManager))
            {
                this.OnSourceCollectionChanged(sender, (NotifyCollectionChangedEventArgs)e);
            }
            else
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Private Fields

        // to save memory, store bool variables in a bit vector
        private BitVector32 _cacheBits = new BitVector32(0);

        #endregion

        #region SourceAccess Nested Type

        private sealed class SourceAccess : IDisposable
        {
            public SourceAccess(ZoomboxViewStack viewStack)
            {
                _viewStack = viewStack;
                _viewStack.IsChangeFromSource = true;
            }

            ~SourceAccess()
            {
                this.Dispose();
            }

            public void Dispose()
            {
                _viewStack.IsChangeFromSource = false;
                _viewStack = null;
                GC.SuppressFinalize(this);
            }

            private ZoomboxViewStack _viewStack;
        }

        #endregion

        #region CacheBits Nested Type

        private enum CacheBits
        {
            AreViewsFromSource = 0x00000001,
            IsChangeFromSource = 0x00000002,
            IsResettingViews = 0x00000004,
            IsMovingViews = 0x00000008,
            IsSettingInitialViewAfterClear = 0x00000010,
        }

        #endregion
    }
}