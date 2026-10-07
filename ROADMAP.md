# Roadmap

### 🌟 Core Features & Functionality
1. **Multiple Stocks Support:** Allow users to track a watchlist of multiple stocks. The widget could auto-rotate through them (carousel style) every few seconds, or display a compact vertical list.
2. **Crypto & Forex Support:** Extend the ticker settings to support cryptocurrencies (e.g., `BTC-USD`) and currency exchange rates, making it an all-in-one financial tracker.
3. **Price Alerts:** Add a feature in the settings to set a "Target Price" or "Stop Loss." If the stock hits that price, Stocky could show a Windows notification or gently pulse the widget's border.
4. **Custom Refresh Intervals:** Let the user decide how often the data fetches (e.g., every 1 min, 5 mins, 15 mins) to save bandwidth or get faster updates.

### 🎨 UI & Customization
5. **Dark Mode / Themes:** Add an automatic Dark/Light mode that syncs with Windows system settings, or allow the user to pick custom accent colors for the widget card.
6. **Adjustable Opacity (Glassmorphism):** Add a slider in the settings to make the widget background semi-transparent with a blur effect (Acrylic/Mica), allowing it to blend even better with the desktop wallpaper.
7. **Interactive Sparkline:** When hovering over the sparkline chart, show a tiny tooltip with the exact price and time at that specific point in the day.

### ⚙️ System Integration
8. **System Tray Menu:** Add an icon to the Windows System Tray (bottom right corner near the clock). Right-clicking it could allow the user to quickly Hide/Show the widget, open settings, or Exit completely.
9. **Multiple Widget Instances:** Allow the user to spawn multiple individual widgets and drag them around the desktop to build a custom dashboard of their favorite tickers.
10. **Drag-and-Drop Repositioning:** Right now it sits in the top right. You could add a small drag handle so the user can place the widget anywhere on their desktop, saving the X/Y coordinates to `settings.json`.