Categories
GET /categories
The list of available categories.

A Category represents Blueprints with uniform characteristics. For examples: "single cards", "boosters", "dice", "strategic guides". There are approximately thirty Categories.

Accepted parameters:

game_id
integer	Optional	Filter results by game_id
GET /categories
          
curl https://api.cardtrader.com/api/v2/categories \
-H "Authorization: Bearer [YOUR_AUTH_TOKEN]"

        
The Category object
id
integer		A unique Category identifier
name
string		The Category name
game_id
integer		The ID of the Game to which the Category belongs
properties
[Property]		An array of Property objects
Example Response
[
  {
    "id": 1,
    "name": "Magic Single Card",
    "game_id": 1,
    "properties": + Properties
  }
]