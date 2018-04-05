package com.microsoft.azure;

public class Item {
	
		public String theColor;		
	 	public double thePrice;	    
	    public String ItemCategory;	    	    
	
	    public Item() {
		}

	    public Item(int color, int price, int ItmCat) {
	        this.setColor(color);
	        this.setPrice(price);	 	        
	        this.setItemCategory(ItmCat);
	    }

	    public String getColor() {
	        return theColor;
	    }

	    public double getPrice() {
	        return thePrice;
	    }

	    public String getItemCategory() {
	        return ItemCategory;
	    }
	    
	    public void setColor(int color) {
	    	String[] Color = {"Red","Green","Blue","Orange","Yellow"};
	    	this.theColor = Color[color];
	    }

	    public void setPrice(int price) {
	    	double[] Price = {1.4,2.3,3.2,4.1,5.1};
	    	this.thePrice = Price[price]; 
	    }

	    public void setItemCategory(int ItmCat) {
	    	 String[] CategoryList = {"Vegetables","Beverage","Meat","Bread","Other"};
	    	 this.ItemCategory = CategoryList[ItmCat];
	    }	   
}
