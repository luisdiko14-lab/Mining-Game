package dev.blockcraft.tools;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.IOException;
import java.io.Reader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

/** Validates BlockCraft's shared JSON content pack before the C# game is built. */
public final class ContentPackAudit {
    private ContentPackAudit() { }
    public static void main(String[] args) throws IOException {
        Path root = args.length == 0 ? Path.of(".") : Path.of(args[0]);
        Path data = root.resolve("assets").resolve("data");
        JsonArray blocks = readArray(data.resolve("blocks.json")), items = readArray(data.resolve("items.json"));
        JsonArray recipes = readArray(data.resolve("recipes.json")), smelting = readArray(data.resolve("smelting.json"));
        Map<Integer, JsonObject> blocksById = new HashMap<>();
        Map<String, JsonObject> blocksByName = new HashMap<>();
        for (JsonElement element : blocks) {
            JsonObject block=element.getAsJsonObject(); int id=requiredInt(block,"id"); String name=requiredString(block,"name");
            unique(blocksById.putIfAbsent(id,block)==null,"duplicate block id "+id);
            unique(blocksByName.putIfAbsent(name,block)==null,"duplicate block name "+name);
            if(block.has("lightEmission")){int light=block.get("lightEmission").getAsInt();require(light>=0&&light<=15,name+" lightEmission must be in 0..15");}
        }
        Map<Integer,JsonObject> itemsById=new HashMap<>(); Map<String,JsonObject> itemsByName=new HashMap<>();
        for(JsonElement element:items){
            JsonObject item=element.getAsJsonObject();int id=requiredInt(item,"id");String name=requiredString(item,"name");
            unique(itemsById.putIfAbsent(id,item)==null,"duplicate item id "+id);
            unique(itemsByName.putIfAbsent(name,item)==null,"duplicate item name "+name);
            int max=item.has("maxStack")?item.get("maxStack").getAsInt():64;require(max>=1&&max<=999,name+" has invalid maxStack");
            if(item.has("placesBlockId")){int placed=item.get("placesBlockId").getAsInt();if(placed!=0)require(blocksById.containsKey(placed),name+" places unknown block "+placed);}
        }
        require(blocksById.containsKey(0)&&"air".equals(requiredString(blocksById.get(0),"name")),"block id 0 must be air");
        for(JsonObject block:blocksById.values()){int drop=block.has("dropItemId")?block.get("dropItemId").getAsInt():0;if(drop!=0)require(itemsById.containsKey(drop),requiredString(block,"name")+" drops unknown item "+drop);}
        for(JsonElement element:recipes){
            JsonObject recipe=element.getAsJsonObject();String rid=recipe.has("id")?recipe.get("id").getAsString():"unnamed recipe";
            JsonArray pattern=recipe.getAsJsonArray("pattern");require(pattern.size()>=1&&pattern.size()<=3,rid+" pattern height must fit 1..3");
            int width=pattern.get(0).getAsString().length();require(width>=1&&width<=3,rid+" pattern width must fit 1..3");
            Set<Character> used=new HashSet<>();
            for(JsonElement row:pattern){String value=row.getAsString();require(value.length()==width,rid+" pattern rows must have equal width");for(char c:value.toCharArray())if(c!=' ')used.add(c);}
            JsonObject ingredients=recipe.getAsJsonObject("ingredients");
            for(Map.Entry<String,JsonElement> ingredient:ingredients.entrySet()){
                require(ingredient.getKey().length()==1,rid+" ingredient keys must be one character");
                String name=ingredient.getValue().getAsString();require(itemsByName.containsKey(name),rid+" uses unknown item "+name);
            }
            for(char c:used)require(ingredients.has(String.valueOf(c)),rid+" has unmapped ingredient "+c);
            JsonObject result=recipe.getAsJsonObject("result");String output=requiredString(result,"item");
            require(itemsByName.containsKey(output),rid+" creates unknown item "+output);
            int count=requiredInt(result,"count");int max=itemsByName.get(output).has("maxStack")?itemsByName.get(output).get("maxStack").getAsInt():64;
            require(count>=1&&count<=max,rid+" result stack exceeds "+output+" limit");
        }
        for(JsonElement element:smelting){
            JsonObject recipe=element.getAsJsonObject();String input=requiredString(recipe,"input"),output=requiredString(recipe,"output");
            require(itemsByName.containsKey(input),"smelting uses unknown input "+input);require(itemsByName.containsKey(output),"smelting creates unknown output "+output);
            int count=requiredInt(recipe,"count"),max=itemsByName.get(output).has("maxStack")?itemsByName.get(output).get("maxStack").getAsInt():64;
            float duration=recipe.get("durationSeconds").getAsFloat();require(count>=1&&count<=max,"invalid smelting output count for "+output);
            require(Float.isFinite(duration)&&duration>0f,"invalid smelting duration for "+input);
        }
        System.out.printf("BlockCraft content audit passed: %d blocks, %d items, %d crafting recipes, %d smelting recipes.%n",blocks.size(),items.size(),recipes.size(),smelting.size());
    }
    private static JsonArray readArray(Path file)throws IOException{try(Reader reader=Files.newBufferedReader(file)){JsonElement root=JsonParser.parseReader(reader);require(root.isJsonArray(),file.getFileName()+" must contain a JSON array");return root.getAsJsonArray();}}
    private static int requiredInt(JsonObject object,String property){require(object.has(property),"missing property "+property);return object.get(property).getAsInt();}
    private static String requiredString(JsonObject object,String property){require(object!=null&&object.has(property)&&!object.get(property).isJsonNull(),"missing property "+property);return object.get(property).getAsString();}
    private static void unique(boolean condition,String message){require(condition,message);}
    private static void require(boolean condition,String message){if(!condition)throw new IllegalStateException("Content-pack validation failed: "+message);}
}