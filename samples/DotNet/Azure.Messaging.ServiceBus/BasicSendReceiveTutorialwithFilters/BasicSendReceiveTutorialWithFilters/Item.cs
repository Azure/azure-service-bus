using System;
using System.Collections.Generic;
using System.Text;

namespace BasicSendReceiveTutorialWithFilters
{
    class Item
    {
        public string theColor;
        public double thePrice;
        public string ItemCategory;

        public Item()
        {
        }

        public Item(int color, int price, int ItmCat)
        {
            this.SetColor(color);
            this.SetPrice(price);
            this.SetItemCategory(ItmCat);
        }

        public string GetColor()
        {
            return theColor;
        }

        public double GetPrice()
        {
            return thePrice;
        }

        public string GetItemCategory()
        {
            return ItemCategory;
        }

        public void SetColor(int color)
        {
            string[] Color = { "Red", "Green", "Blue", "Orange", "Yellow" };
            this.theColor = Color[color];
        }

        public void SetPrice(int price)
        {
            double[] Price = { 1.4, 2.3, 3.2, 4.1, 5.1 };
            this.thePrice = Price[price];
        }

        public void SetItemCategory(int ItmCat)
        {
            string[] CategoryList = { "Vegetables", "Beverage", "Meat", "Bread", "Other" };
            this.ItemCategory = CategoryList[ItmCat];
        }
    }
}
