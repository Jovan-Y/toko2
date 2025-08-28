using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace InventoryManager
{
    public partial class PriceLabelPrintWindow : Window, INotifyPropertyChanged
    {
        // --- DATA COLLECTIONS ---
        private List<PrintQueueItem> _allPrintableItems = new List<PrintQueueItem>();
        public ObservableCollection<PrintQueueItem> ProductsList { get; set; }
        public ObservableCollection<UnitToPrint> UnitsForSelectedProduct { get; set; }
        public ObservableCollection<PrintQueueItem> PrintQueue { get; set; }
        public ObservableCollection<PaperSize> PaperSizes { get; set; }
        public ObservableCollection<string> LabelSizes { get; set; }

        private readonly string _settingsFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print_settings.conf");
        private const double MmToDip = 96.0 / 25.4;

        private int _printQueueTotalItems;
        public string PrintQueueTitle => $"Daftar Cetak ({_printQueueTotalItems} item)";

        // --- PAGINATION FIELDS ---
        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _labelsPerPage = 1;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PriceLabelPrintWindow()
        {
            InitializeComponent();
            DataContext = this;

            // --- INITIALIZE COLLECTIONS ---
            ProductsList = new ObservableCollection<PrintQueueItem>();
            UnitsForSelectedProduct = new ObservableCollection<UnitToPrint>();
            PrintQueue = new ObservableCollection<PrintQueueItem>();
            PaperSizes = new ObservableCollection<PaperSize>();
            LabelSizes = new ObservableCollection<string>();

            // --- SET DATASOURCES ---
            ProductsListView.ItemsSource = ProductsList;
            UnitsToPrintListView.ItemsSource = UnitsForSelectedProduct;
            PrintQueueDataGrid.ItemsSource = PrintQueue;
            PaperSizeComboBox.ItemsSource = PaperSizes;
            LabelSizeComboBox.ItemsSource = LabelSizes;

            // --- EVENT HANDLERS ---
            PrintQueue.CollectionChanged += (s, e) => { UpdateTotalItems(); GeneratePreview(); };
            Loaded += PriceLabelPrintWindow_Loaded;
            Closing += PriceLabelPrintWindow_Closing;
        }

        private void PriceLabelPrintWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeSizes();
            LoadSettings();
            LoadDataFromMainWindow();
            FilterProducts("");
            GeneratePreview();
        }

        private void PriceLabelPrintWindow_Closing(object? sender, CancelEventArgs e)
        {
            SaveSettings();
        }

        private void InitializeSizes()
        {
            PaperSizes.Add(new PaperSize("A4", 210, 297));
            PaperSizes.Add(new PaperSize("Letter", 216, 279));
            PaperSizes.Add(new PaperSize("Blueprint 80mm", 80, 165));
            PaperSizes.Add(new PaperSize("Blueprint 80x40mm", 80, 180));
            PaperSizeComboBox.SelectedIndex = 0;

            LabelSizes.Add("30x15");
            LabelSizes.Add("50x25");
            LabelSizes.Add("80x40");
            LabelSizes.Add("75x26");
            LabelSizes.Add("75x28");
            LabelSizeComboBox.SelectedIndex = 1;
        }

        private void LoadDataFromMainWindow()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            var allProducts = mainWindow.GetAllProductsForAnalysis();
            var allConversions = mainWindow.GetAllConversionsForAnalysis();

            _allPrintableItems = allConversions
                .Where(c => c.SellingPrice > 0)
                .Select(c => new PrintQueueItem
                {
                    ProductName = allProducts.FirstOrDefault(p => p.ProductId == c.ProductID)?.ProductName ?? "N/A",
                    UnitName = c.UnitName,
                    Price = c.SellingPrice
                }).ToList();
        }

        #region UI Event Handlers

        private void SearchProductTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterProducts(SearchProductTextBox.Text);
        }

        private void RefreshProductsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDataFromMainWindow();
            FilterProducts(SearchProductTextBox.Text);
        }

        private void ProductsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UnitsForSelectedProduct.Clear();
            if (ProductsListView.SelectedItem is PrintQueueItem selectedProduct)
            {
                var units = _allPrintableItems
                    .Where(item => item.ProductName == selectedProduct.ProductName)
                    .Select(item => new UnitToPrint
                    {
                        DisplayName = $"{item.UnitName} ({item.Price:C0})",
                        UnitName = item.UnitName,
                        Price = item.Price,
                        IsSelected = true
                    });

                foreach (var unit in units)
                {
                    UnitsForSelectedProduct.Add(unit);
                }
            }
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsListView.SelectedItem is not PrintQueueItem selectedProduct) return;

            var selectedUnits = UnitsForSelectedProduct.Where(u => u.IsSelected).ToList();
            if (!selectedUnits.Any())
            {
                MessageBox.Show("Pilih minimal satu satuan untuk ditambahkan.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newItem = new PrintQueueItem
            {
                ProductName = selectedProduct.ProductName,
                Copies = 1,
                Units = selectedUnits.Select(u => new UnitPricingInfo { UnitName = u.UnitName, Price = u.Price }).ToList()
            };

            PrintQueue.Add(newItem);

            foreach (var unit in UnitsForSelectedProduct)
            {
                unit.IsSelected = false;
            }
            UpdateTotalItems();
        }

        private void RemoveItemButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is PrintQueueItem item)
            {
                PrintQueue.Remove(item);
            }
        }

        private void Copies_TextChanged(object sender, TextChangedEventArgs e) => GeneratePreview();
        private void Settings_Changed(object sender, RoutedEventArgs e) => GeneratePreview();

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrintQueue.Any(i => i.Copies > 0))
            {
                MessageBox.Show("Antrian cetak kosong.", "Informasi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var paperSize = GetCurrentPaperSize();
            var labelSize = GetCurrentLabelSize();

            if (paperSize == null)
            {
                MessageBox.Show("Ukuran kertas belum dipilih.", "Input Tidak Valid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (labelSize == null)
            {
                MessageBox.Show("Ukuran label belum dipilih.", "Input Tidak Valid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                var paginator = new LabelPaginator(PrintQueue.ToList(), paperSize, labelSize, GetCurrentMargin());
                printDialog.PrintDocument(paginator, "Cetak Label Harga");
            }
        }

        #endregion

        #region Pagination Controls
        private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                GeneratePreview();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                GeneratePreview();
            }
        }

        #endregion

        #region Size Management & Settings

        private void AddPaperSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(PaperWidthTextBox.Text, out double w) && double.TryParse(PaperHeightTextBox.Text, out double h) && w > 0 && h > 0)
            {
                var newSize = new PaperSize($"Kustom {w}x{h}", w, h);
                if (!PaperSizes.Any(p => p.Name == newSize.Name)) PaperSizes.Add(newSize);
                PaperSizeComboBox.SelectedItem = PaperSizes.FirstOrDefault(p => p.Name == newSize.Name);
            }
        }

        private void AddLabelSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(LabelWidthTextBox.Text, out double w) && double.TryParse(LabelHeightTextBox.Text, out double h) && w > 0 && h > 0)
            {
                string newSize = $"{w}x{h}";
                if (!LabelSizes.Contains(newSize)) LabelSizes.Add(newSize);
                LabelSizeComboBox.SelectedItem = newSize;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    ["LabelSize"] = LabelSizeComboBox.SelectedItem?.ToString() ?? "50x25",
                    ["Margin"] = MarginTextBox.Text,
                    ["ProductNameFont"] = ProductNameFontSizeTextBox.Text,
                    ["PriceFont"] = PriceFontSizeTextBox.Text
                };
                File.WriteAllLines(_settingsFilePath, settings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }
            catch (Exception ex) { Console.WriteLine("Gagal menyimpan pengaturan: " + ex.Message); }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;
                var settings = File.ReadAllLines(_settingsFilePath).ToDictionary(l => l.Split('=')[0], l => l.Split('=')[1]);

                if (settings.TryGetValue("LabelSize", out var labelSize)) LabelSizeComboBox.SelectedItem = labelSize;
                if (settings.TryGetValue("Margin", out var margin)) MarginTextBox.Text = margin;
                if (settings.TryGetValue("ProductNameFont", out var pNameFont)) ProductNameFontSizeTextBox.Text = pNameFont;
                if (settings.TryGetValue("PriceFont", out var priceFont)) PriceFontSizeTextBox.Text = priceFont;
            }
            catch (Exception ex) { Console.WriteLine("Gagal memuat pengaturan: " + ex.Message); }
        }

        #endregion

        #region Helper & Preview Methods

        private void FilterProducts(string searchTerm)
        {
            ProductsList.Clear();
            var filtered = string.IsNullOrWhiteSpace(searchTerm)
                ? _allPrintableItems
                : _allPrintableItems.Where(p => p.ProductName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

            foreach (var p in filtered.GroupBy(p => p.ProductName).Select(g => g.First()))
            {
                ProductsList.Add(p);
            }
        }

        private void UpdateTotalItems()
        {
            _printQueueTotalItems = PrintQueue.Sum(item => item.Copies);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrintQueueTitle)));
        }

        private void GeneratePreview()
        {
            if (!IsLoaded) return;
            PreviewCanvas.Children.Clear();

            var paper = GetCurrentPaperSize();
            var labelSizeStr = GetCurrentLabelSize();
            double margin_mm = GetCurrentMargin();

            if (paper == null || labelSizeStr == null) return;
            if (!TryParseMm(labelSizeStr, out double labelW_mm, out double labelH_mm)) return;

            double scale = Math.Min(500 / paper.Width, 600 / paper.Height);
            PaperPreviewBorder.Width = paper.Width * scale;
            PaperPreviewBorder.Height = paper.Height * scale;
            PreviewCanvas.Width = paper.Width * scale;
            PreviewCanvas.Height = paper.Height * scale;

            double printableWidth = paper.Width - (2 * margin_mm);
            double printableHeight = paper.Height - (2 * margin_mm);

            int cols = (int)(printableWidth / labelW_mm);
            int rows = (int)(printableHeight / labelH_mm);

            _labelsPerPage = cols * rows;

            if (_labelsPerPage < 1)
            {
                FitInfoTextBlock.Text = "Ukuran label terlalu besar untuk kertas.";
                return;
            }

            var allItemsToPrint = PrintQueue.SelectMany(item => Enumerable.Repeat(item, item.Copies)).ToList();
            _totalPages = (int)Math.Ceiling((double)allItemsToPrint.Count / _labelsPerPage);
            if (_totalPages == 0) _totalPages = 1;
            if (_currentPage > _totalPages) _currentPage = _totalPages;

            FitInfoTextBlock.Text = $"Muat: {_labelsPerPage} label per halaman ({cols} kolom x {rows} baris).";
            PageInfoTextBlock.Text = $"Halaman {_currentPage} dari {_totalPages}";
            PreviousPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;

            var itemsToPreview = allItemsToPrint.Skip((_currentPage - 1) * _labelsPerPage).Take(_labelsPerPage).ToList();

            if (!itemsToPreview.Any() && allItemsToPrint.Any())
            {
                _currentPage = 1;
                itemsToPreview = allItemsToPrint.Skip(0).Take(_labelsPerPage).ToList();
            }

            if (!itemsToPreview.Any())
            {
                itemsToPreview.Add(new PrintQueueItem
                {
                    ProductName = "Nama Produk",
                    Units = new List<UnitPricingInfo> { new UnitPricingInfo { UnitName = "Pcs", Price = 10000 } }
                });
            }

            for (int i = 0; i < itemsToPreview.Count; i++)
            {
                var item = itemsToPreview[i];
                var labelVisual = CreateLabelVisual(item);

                var viewBox = new Viewbox
                {
                    Width = labelW_mm * scale,
                    Height = labelH_mm * scale,
                    Child = labelVisual,
                    Stretch = Stretch.Fill
                };

                Canvas.SetLeft(viewBox, (margin_mm + (i % cols) * labelW_mm) * scale);
                Canvas.SetTop(viewBox, (margin_mm + (i / cols) * labelH_mm) * scale);
                PreviewCanvas.Children.Add(viewBox);
            }
        }

        private FrameworkElement CreateLabelVisual(PrintQueueItem item)
        {
            var border = new Border
            {
                Padding = new Thickness(4),
                Background = Brushes.White
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            double.TryParse(ProductNameFontSizeTextBox.Text, out double pNameFont);
            double.TryParse(PriceFontSizeTextBox.Text, out double priceFont);

            var productNameBlock = new TextBlock
            {
                Text = item.ProductName,
                FontWeight = FontWeights.Bold,
                FontSize = pNameFont > 0 ? pNameFont : 12,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            Grid.SetRow(productNameBlock, 0);
            mainGrid.Children.Add(productNameBlock);

            var pricePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(pricePanel, 1);
            mainGrid.Children.Add(pricePanel);

            foreach (var unit in item.Units)
            {
                var priceBlock = new TextBlock
                {
                    Text = $"{unit.Price:C0} / {unit.UnitName}",
                    FontSize = priceFont > 0 ? priceFont : 14,
                    FontWeight = FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                pricePanel.Children.Add(priceBlock);
            }

            var centeringGrid = new Grid { Children = { mainGrid } };
            mainGrid.VerticalAlignment = VerticalAlignment.Center;

            border.Child = centeringGrid;
            return border;
        }

        private bool TryParseMm(string text, out double width, out double height)
        {
            width = 0; height = 0;
            var parts = text.ToLower().Split('x');
            if (parts.Length != 2) return false;
            return double.TryParse(parts[0], out width) && double.TryParse(parts[1], out height);
        }

        private PaperSize GetCurrentPaperSize() => PaperSizeComboBox.SelectedItem as PaperSize;
        private string GetCurrentLabelSize() => LabelSizeComboBox.SelectedItem as string;
        private double GetCurrentMargin() => double.TryParse(MarginTextBox.Text, out double m) ? m : 0;

        #endregion

        #region Data Classes

        public class PrintQueueItem : INotifyPropertyChanged
        {
            public string ProductName { get; set; } = "";
            public string UnitName { get; set; } = "";
            public decimal Price { get; set; }
            public List<UnitPricingInfo> Units { get; set; } = new List<UnitPricingInfo>();

            private int _copies;
            public int Copies
            {
                get => _copies;
                set { _copies = value; OnPropertyChanged(nameof(Copies)); }
            }

            public string DisplayName => $"{ProductName} ({(Units.Any() ? string.Join(", ", Units.Select(u => u.UnitName)) : UnitName)})";
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class UnitPricingInfo
        {
            public string UnitName { get; set; } = "";
            public decimal Price { get; set; }
        }

        public class UnitToPrint
        {
            public string DisplayName { get; set; } = "";
            public string UnitName { get; set; } = "";
            public decimal Price { get; set; }
            public bool IsSelected { get; set; }
        }

        public class PaperSize
        {
            public string Name { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public PaperSize(string name, double width, double height) { Name = name; Width = width; Height = height; }
            public override string ToString() => $"{Name} ({Width}x{Height} mm)";
        }

        #endregion

        #region Paginator Class

        public class LabelPaginator : DocumentPaginator
        {
            private readonly List<PrintQueueItem> _items;
            private readonly Size _paperSize;
            private readonly Size _labelSize;
            private readonly double _margin;
            private readonly int _labelsPerPage;
            private readonly int _pageCount;

            public LabelPaginator(List<PrintQueueItem> items, PaperSize paper, string labelSizeStr, double margin)
            {
                _items = items.SelectMany(item => Enumerable.Repeat(item, item.Copies)).ToList();
                _paperSize = new Size(paper.Width * MmToDip, paper.Height * MmToDip);

                TryParseMm(labelSizeStr, out double labelW, out double labelH);
                _labelSize = new Size(labelW * MmToDip, labelH * MmToDip);
                _margin = margin * MmToDip;

                int cols = (int)((_paperSize.Width - (2 * _margin)) / _labelSize.Width);
                int rows = (int)((_paperSize.Height - (2 * _margin)) / _labelSize.Height);
                _labelsPerPage = cols * rows > 0 ? cols * rows : 1;
                _pageCount = (int)Math.Ceiling((double)_items.Count / _labelsPerPage);
            }

            public override DocumentPage GetPage(int pageNumber)
            {
                var canvas = new Canvas();

                int startIndex = pageNumber * _labelsPerPage;
                var itemsOnPage = _items.Skip(startIndex).Take(_labelsPerPage).ToList();
                int cols = (int)((_paperSize.Width - (2 * _margin)) / _labelSize.Width);
                if (cols == 0) cols = 1;

                for (int i = 0; i < itemsOnPage.Count; i++)
                {
                    var item = itemsOnPage[i];
                    var labelVisual = CreateLabelVisual(item);

                    double x = _margin + (i % cols) * _labelSize.Width;
                    double y = _margin + (i / cols) * _labelSize.Height;

                    Canvas.SetLeft(labelVisual, x);
                    Canvas.SetTop(labelVisual, y);
                    canvas.Children.Add(labelVisual);
                }

                canvas.Measure(_paperSize);
                canvas.Arrange(new Rect(_paperSize));

                return new DocumentPage(canvas);
            }

            public override bool IsPageCountValid => true;
            public override int PageCount => _pageCount;
            public override Size PageSize { get => _paperSize; set => throw new NotImplementedException(); }
            public override IDocumentPaginatorSource Source => null;

            private FrameworkElement CreateLabelVisual(PrintQueueItem item)
            {
                var border = new Border
                {
                    Width = _labelSize.Width,
                    Height = _labelSize.Height,
                    Background = Brushes.White,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(2)
                };

                var mainStackPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var productNameBlock = new TextBlock
                {
                    Text = item.ProductName,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                mainStackPanel.Children.Add(productNameBlock);

                foreach (var unit in item.Units)
                {
                    var priceBlock = new TextBlock
                    {
                        Text = $"{unit.Price:C0} / {unit.UnitName}",
                        FontSize = 14,
                        FontWeight = FontWeights.Normal,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center
                    };
                    mainStackPanel.Children.Add(priceBlock);
                }

                var viewBox = new Viewbox
                {
                    Child = mainStackPanel,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly
                };

                border.Child = viewBox;

                border.Measure(new Size(_labelSize.Width, _labelSize.Height));
                border.Arrange(new Rect(new Size(_labelSize.Width, _labelSize.Height)));

                return border;
            }

            private bool TryParseMm(string text, out double width, out double height)
            {
                width = 0; height = 0;
                var parts = text.ToLower().Split('x');
                if (parts.Length != 2) return false;
                return double.TryParse(parts[0], out width) && double.TryParse(parts[1], out height);
            }
        }
        #endregion
    }
}
