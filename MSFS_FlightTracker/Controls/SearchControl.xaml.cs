﻿using MapControl;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace MSFS_FlightTracker
{
    /// <summary>Provides data for the Navigate event.</summary>
    public sealed class NavigateEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of the NavigateEventArgs class.</summary>
        /// <param name="result">The SearchResult to navigate to.</param>
        internal NavigateEventArgs(SearchResult result)
        {
            this.Result = result;
        }

        /// <summary>Gets the selected SearchResult.</summary>
        public SearchResult Result { get; private set; }
    }

    /// <summary>Provides a UI for the SearchProvider.</summary>
    public sealed partial class SearchControl : UserControl
    {
        /// <summary>Identifies the SearchArea dependency property.</summary>
        public static readonly DependencyProperty SearchAreaProperty =
            DependencyProperty.Register("SearchArea", typeof(Rect), typeof(SearchControl));

        private SearchProvider _provider = new SearchProvider();

        /// <summary>Initializes a new instance of the SearchControl class.</summary>
        public SearchControl()
        {
            this.InitializeComponent();
            _provider.SearchCompleted += this.OnSearchCompleted;
            _provider.SearchError += this.OnSearchError;
        }

        /// <summary>Occurs when a SearchResult is selected.</summary>
        public event EventHandler<NavigateEventArgs> Navigate;

        /// <summary>Gets or sets the area to localize search results.</summary>
        public Rect SearchArea
        {
            get { return (Rect)this.GetValue(SearchAreaProperty); }
            set { this.SetValue(SearchAreaProperty, value); }
        }

        private void ClearResults(string status)
        {
            this.resultsArea.Visibility = Visibility.Collapsed;
            this.searchBtn.IsEnabled = true;
            this.searchResults.ItemsSource = null;
            this.statusLabel.Text = status;
        }

        private void OnClearResultsClick(object sender, RoutedEventArgs e)
        {
            this.ClearResults(string.Empty);
            this.NavigateTo(null);
        }

        private void OnResultButtonClick(object sender, RoutedEventArgs e)
        {
            SearchResult result = ((Button)sender).DataContext as SearchResult;
            if (result != null)
            {
                this.NavigateTo(result);
            }
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            this.resultsArea.Visibility = Visibility.Collapsed;
            string search = (this.searchBox.Text ?? string.Empty).Trim();
            if (search.Length == 0)
            {
                this.statusLabel.Text = "Please enter a search term to find or a longitude and latitude to navigate to.";
                return;
            }
            if (_provider.Search(search, this.SearchArea))
            {
                this.searchBtn.IsEnabled = false; // Only one search at a time
                this.statusLabel.Text = "Searching...";
            }
        }

        private void OnSearchCompleted(object sender, EventArgs e)
        {
            if (this.Dispatcher.Thread == Thread.CurrentThread)
            {
                this.ClearResults("No results found.");
                var results = _provider.Results;
                if (results.Length != 0)
                {
                    this.searchResults.ItemsSource = results;
                    this.statusLabel.Text = string.Empty;
                    this.resultsArea.Visibility = Visibility.Visible;
                    this.resultsArea.IsExpanded = true;
                    if (results.Length == 1) // If there's only one result then just navigate to it
                    {
                        this.NavigateTo(results[0]);
                    }
                }
            }
            else
            {
                this.Dispatcher.Invoke(new Action<object, EventArgs>(this.OnSearchCompleted), sender, e);
            }
        }

        private void OnSearchError(object sender, SearchErrorEventArgs e)
        {
            if (this.Dispatcher.Thread == Thread.CurrentThread)
            {
                this.ClearResults(e.Error);
            }
            else
            {
                this.Dispatcher.BeginInvoke(new Action<object, SearchErrorEventArgs>(this.OnSearchError), sender, e);
            }
        }

        private void NavigateTo(SearchResult result)
        {
            this.resultsArea.IsExpanded = false;
            var callback = this.Navigate;
            if (callback != null)
            {
                callback(this, new NavigateEventArgs(result));
            }
        }
    }
}