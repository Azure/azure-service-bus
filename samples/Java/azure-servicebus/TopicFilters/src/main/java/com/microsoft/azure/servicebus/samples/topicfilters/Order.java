package com.microsoft.azure.servicebus.samples.topicfilters;

public class Order {

    public Order() {
    }

    public Order(String color, int quantity, String priority) {
        this.color = color;
        this.quantity = quantity;
        this.priority = priority;
    }

    public String getColor() {
        return color;
    }

    public String getPriority() {
        return priority;
    }

    public void setPriority(String priority) {
        this.priority = priority;
    }

    public int getQuantity() {

        return quantity;
    }

    public void setQuantity(int quantity) {
        this.quantity = quantity;
    }

    public void setColor(String color) {
        this.color = color;
    }

    String color;
    int quantity;
    String priority;
}
