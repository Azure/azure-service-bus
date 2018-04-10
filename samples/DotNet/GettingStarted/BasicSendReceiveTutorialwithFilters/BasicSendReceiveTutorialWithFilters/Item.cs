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
            this.setColor(color);
            this.setPrice(price);
            this.setItemCategory(ItmCat);
        }

        public string getColor()
        {
            return theColor;
        }

        public double getPrice()
        {
            return thePrice;
        }

        public string getItemCategory()
        {
            return ItemCategory;
        }

        public void setColor(int color)
        {
            string[] Color = { "Red", "Green", "Blue", "Orange", "Yellow" };
            this.theColor = Color[color];
        }

        public void setPrice(int price)
        {
            double[] Price = { 1.4, 2.3, 3.2, 4.1, 5.1 };
            this.thePrice = Price[price];
        }

        public void setItemCategory(int ItmCat)
        {
            string[] CategoryList = { "Vegetables", "Beverage", "Meat", "Bread", "Other" };
            this.ItemCategory = CategoryList[ItmCat];
        }
    }
}
