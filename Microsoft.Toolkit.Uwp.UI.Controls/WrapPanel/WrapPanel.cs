﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Microsoft.Toolkit.Uwp.UI.Controls
{
    /// <summary>
    /// WrapPanel is a panel that position child control vertically or horizontally based on the orientation and when max width / max height is reached a new row (in case of horizontal) or column (in case of vertical) is created to fit new controls.
    /// </summary>
    public partial class WrapPanel : Panel
    {
        /// <summary>
        /// Gets or sets a uniform Horizontal distance (in pixels) between items when <see cref="Orientation"/> is set to Horizontal,
        /// or between columns of items when <see cref="Orientation"/> is set to Vertical.
        /// </summary>
        public double HorizontalSpacing
        {
            get { return (double)GetValue(HorizontalSpacingProperty); }
            set { SetValue(HorizontalSpacingProperty, value); }
        }

        /// <summary>
        /// Identifies the <see cref="HorizontalSpacing"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty HorizontalSpacingProperty =
            DependencyProperty.Register(
                nameof(HorizontalSpacing),
                typeof(double),
                typeof(WrapPanel),
                new PropertyMetadata(0d, LayoutPropertyChanged));

        /// <summary>
        /// Gets or sets a uniform Vertical distance (in pixels) between items when <see cref="Orientation"/> is set to Vertical,
        /// or between rows of items when <see cref="Orientation"/> is set to Horizontal.
        /// </summary>
        public double VerticalSpacing
        {
            get { return (double)GetValue(VerticalSpacingProperty); }
            set { SetValue(VerticalSpacingProperty, value); }
        }

        /// <summary>
        /// Identifies the <see cref="VerticalSpacing"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty VerticalSpacingProperty =
            DependencyProperty.Register(
                nameof(VerticalSpacing),
                typeof(double),
                typeof(WrapPanel),
                new PropertyMetadata(0d, LayoutPropertyChanged));

        /// <summary>
        /// Gets or sets the vertical alignment to use when the control <see cref="Orientation"/> is set to <see cref="Orientation.Horizontal"/>.
        /// </summary>
        public VerticalAlignment VerticalContentAlignment
        {
            get => (VerticalAlignment)GetValue(VerticalContentAlignmentProperty);
            set => SetValue(VerticalContentAlignmentProperty, value);
        }

        /// <summary>
        /// The DP to store the <see cref="VerticalContentAlignment"/> property value.
        /// </summary>
        public static readonly DependencyProperty VerticalContentAlignmentProperty = DependencyProperty.Register(
            nameof(VerticalContentAlignment),
            typeof(VerticalAlignment),
            typeof(WrapPanel),
            new PropertyMetadata(VerticalAlignment.Top));

        /// <summary>
        /// Gets or sets the orientation of the WrapPanel.
        /// Horizontal means that child controls will be added horizontally until the width of the panel is reached, then a new row is added to add new child controls.
        /// Vertical means that children will be added vertically until the height of the panel is reached, then a new column is added.
        /// </summary>
        public Orientation Orientation
        {
            get { return (Orientation)GetValue(OrientationProperty); }
            set { SetValue(OrientationProperty, value); }
        }

        /// <summary>
        /// Identifies the <see cref="Orientation"/> dependency property.
        /// </summary>
        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(
                nameof(Orientation),
                typeof(Orientation),
                typeof(WrapPanel),
                new PropertyMetadata(Orientation.Horizontal, LayoutPropertyChanged));

        /// <summary>
        /// Gets or sets the distance between the border and its child object.
        /// </summary>
        /// <returns>
        /// The dimensions of the space between the border and its child as a Thickness value.
        /// Thickness is a structure that stores dimension values using pixel measures.
        /// </returns>
        public Thickness Padding
        {
            get { return (Thickness)GetValue(PaddingProperty); }
            set { SetValue(PaddingProperty, value); }
        }

        /// <summary>
        /// Identifies the Padding dependency property.
        /// </summary>
        /// <returns>The identifier for the <see cref="Padding"/> dependency property.</returns>
        public static readonly DependencyProperty PaddingProperty =
            DependencyProperty.Register(
                nameof(Padding),
                typeof(Thickness),
                typeof(WrapPanel),
                new PropertyMetadata(default(Thickness), LayoutPropertyChanged));

        /// <summary>
        /// Gets or sets a value indicating how to arrange child items
        /// </summary>
        public StretchChild StretchChild
        {
            get { return (StretchChild)GetValue(StretchChildProperty); }
            set { SetValue(StretchChildProperty, value); }
        }

        /// <summary>
        /// Identifies the <see cref="StretchChild"/> dependency property.
        /// </summary>
        /// <returns>The identifier for the <see cref="StretchChild"/> dependency property.</returns>
        public static readonly DependencyProperty StretchChildProperty =
            DependencyProperty.Register(
                nameof(StretchChild),
                typeof(StretchChild),
                typeof(WrapPanel),
                new PropertyMetadata(StretchChild.None, LayoutPropertyChanged));

        private static void LayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WrapPanel wp)
            {
                wp.InvalidateMeasure();
                wp.InvalidateArrange();
            }
        }

        /// <inheritdoc />
        protected override Size MeasureOverride(Size availableSize)
        {
            availableSize.Width = availableSize.Width - Padding.Left - Padding.Right;
            availableSize.Height = availableSize.Height - Padding.Top - Padding.Bottom;
            var totalMeasure = UvMeasure.Zero;
            var parentMeasure = new UvMeasure(Orientation, availableSize.Width, availableSize.Height);
            var spacingMeasure = new UvMeasure(Orientation, HorizontalSpacing, VerticalSpacing);
            var lineMeasure = UvMeasure.Zero;

            foreach (var child in Children)
            {
                child.Measure(availableSize);
                var currentMeasure = new UvMeasure(Orientation, child.DesiredSize);
                if (currentMeasure.U == 0)
                {
                    continue; // ignore collapsed items
                }

                // if this is the first item, do not add spacing. Spacing is added to the "left"
                double uChange = lineMeasure.U == 0
                    ? currentMeasure.U
                    : currentMeasure.U + spacingMeasure.U;
                if (parentMeasure.U >= uChange + lineMeasure.U)
                {
                    lineMeasure.U += uChange;
                    lineMeasure.V = Math.Max(lineMeasure.V, currentMeasure.V);
                }
                else
                {
                    // new line should be added
                    // to get the max U to provide it correctly to ui width ex: ---| or -----|
                    totalMeasure.U = Math.Max(lineMeasure.U, totalMeasure.U);
                    totalMeasure.V += lineMeasure.V + spacingMeasure.V;

                    // if the next new row still can handle more controls
                    if (parentMeasure.U > currentMeasure.U)
                    {
                        // set lineMeasure initial values to the currentMeasure to be calculated later on the new loop
                        lineMeasure = currentMeasure;
                    }

                    // the control will take one row alone
                    else
                    {
                        // validate the new control measures
                        totalMeasure.U = Math.Max(currentMeasure.U, totalMeasure.U);
                        totalMeasure.V += currentMeasure.V;

                        // add new empty line
                        lineMeasure = UvMeasure.Zero;
                    }
                }
            }

            // update value with the last line
            // if the last loop is (parentMeasure.U > currentMeasure.U + lineMeasure.U) the total isn't calculated then calculate it
            // if the last loop is (parentMeasure.U > currentMeasure.U) the currentMeasure isn't added to the total so add it here
            // for the last condition it is zeros so adding it will make no difference
            // this way is faster than an if condition in every loop for checking the last item
            totalMeasure.U = Math.Max(lineMeasure.U, totalMeasure.U);
            totalMeasure.V += lineMeasure.V;

            totalMeasure.U = Math.Ceiling(totalMeasure.U);

            return Orientation == Orientation.Horizontal ? new Size(totalMeasure.U, totalMeasure.V) : new Size(totalMeasure.V, totalMeasure.U);
        }

        /// <inheritdoc />
        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count > 0)
            {
                var parentMeasure = new UvMeasure(Orientation, finalSize.Width, finalSize.Height);
                var spacingMeasure = new UvMeasure(Orientation, HorizontalSpacing, VerticalSpacing);
                var paddingStart = new UvMeasure(Orientation, Padding.Left, Padding.Top);
                var paddingEnd = new UvMeasure(Orientation, Padding.Right, Padding.Bottom);
                var position = new UvMeasure(Orientation, Padding.Left, Padding.Top);

                var allMeasures = Children.Select(c => new UvMeasure(Orientation, c.DesiredSize)).ToList();
                var allData = new List<(UvMeasure position, UvMeasure size)>();

                var currentV = 0.0;
                void Arrange(int childIndex, bool isLast = false)
                {
                    var child = Children[childIndex];
                    var desiredMeasure = allMeasures[childIndex];
                    if (desiredMeasure.U == 0)
                    {
                        return; // if an item is collapsed, avoid adding the spacing
                    }

                    if ((desiredMeasure.U + position.U + paddingEnd.U) > parentMeasure.U)
                    {
                        // next row!
                        position.U = paddingStart.U;
                        position.V += currentV + spacingMeasure.V;
                        currentV = 0;
                    }

                    // Stretch the last item to fill the available space
                    if (isLast)
                    {
                        desiredMeasure.U = parentMeasure.U - position.U;
                    }

                    allData.Add((position, desiredMeasure));

                    // adjust the location for the next items
                    position.U += desiredMeasure.U + spacingMeasure.U;
                    currentV = Math.Max(desiredMeasure.V, currentV);
                }

                var lastIndex = Children.Count - 1;
                for (var i = 0; i < lastIndex; i++)
                {
                    Arrange(i);
                }

                Arrange(lastIndex, StretchChild == StretchChild.Last);

                // Now that we have all the data, we do the actual arrange pass
                var lastArrangeV = -1.0;
                var maxRowHeight = 0.0;
                for (var i = 0; i < Children.Count; i++)
                {
                    // place the item
                    var child = Children[i];
                    (UvMeasure arrangePosition, UvMeasure arrangeMeasure) = allData[i];

                    if (lastArrangeV != arrangePosition.V)
                    {
                        // We are on a new row, we scan all the items to get the max row height
                        maxRowHeight = allData
                            .Skip(i) // ignore what has already being processed
                            .TakeWhile(d => d.position.V == arrangePosition.V) // we are still on the same row
                            .Max(d => d.size.V); // We want the max.
                    }

                    if (Orientation == Orientation.Horizontal)
                    {
                        switch (VerticalContentAlignment)
                        {
                            case VerticalAlignment.Center:
                            {
                                var vOffset = Math.Max((maxRowHeight - arrangeMeasure.V) / 2.0, 0.0);
                                child.Arrange(new Rect(arrangePosition.U, arrangePosition.V + vOffset, arrangeMeasure.U, arrangeMeasure.V));
                                break;
                            }

                            case VerticalAlignment.Bottom:
                            {
                                var vOffset = Math.Max(maxRowHeight - arrangeMeasure.V, 0.0);
                                child.Arrange(new Rect(arrangePosition.U, arrangePosition.V + vOffset, arrangeMeasure.U, arrangeMeasure.V));
                                break;
                            }

                            case VerticalAlignment.Stretch:
                                child.Arrange(new Rect(arrangePosition.U, arrangePosition.V, arrangeMeasure.U, maxRowHeight));
                                break;
                            case VerticalAlignment.Top:
                            default:
                                child.Arrange(new Rect(arrangePosition.U, arrangePosition.V, arrangeMeasure.U, arrangeMeasure.V));
                                break;
                        }
                    }
                    else
                    {
                        child.Arrange(new Rect(arrangePosition.V, arrangePosition.U, arrangeMeasure.V, arrangeMeasure.U));
                    }
                }
            }

            return finalSize;
        }
    }
}
