Webhooks
If your app is enabled to receive https calls, and you have entered an endpoint, Full API will notify your endpoint whenever an Order is created, modified or deleted.

For example, the endpoint is called:

When another user orders from you
When setting the order from the graphic interface as shipped or changing its status
When another user marks your order as received
id
integer		A unique UUID for each call to your endpoint
time
integer		Request time as an integer number of seconds since the Epoch
cause
'order'.('create' | 'update' | 'destroy')		The cause of the endpoint call. It may be the creation, updating or removal of an Order
object_class
'Order'		The object that was created, updated or removed
object_id
integer		The id of the object
mode
'test' | 'live'		If the webhook is in production (live) or is in a test
data
json		The webhook payload. In the case of create or update, it is the object itself. In the case of destroy, it is empty.

DataPayload
When the webhook is of type order.create or order.update, DataPayload is the object of type Order that has been created or modified.

When the webhook is of type order.destroy, DataPayload is empty.

Check the Webhook Signature
It is possible to verify that the received webhook really comes from CardTrader Full API.

Each original request coming from the Full API is signed by a Signature header, which is the base64 representation of the HMAC digest via sha256 of the request body, signed with the app's shared_secret.


Content-Type: application/json
{
  "id": "c352e8d0-472c-4d02-9c34-915eda5c45b8",
  "time": 1632240962,
  "cause": "order.update",
  "object_class": "Order",
  "object_id": 733733,
  "mode": "live",
  "data": + DataPayload
}
