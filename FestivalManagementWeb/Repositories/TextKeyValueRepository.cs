using FestivalManagementWeb.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Repositories
{
    public class TextKeyValueRepository : ITextKeyValueRepository
    {
        private readonly IMongoCollection<TextKeyValue> _textCollection;

        public TextKeyValueRepository(IMongoDatabase database)
        {
            _textCollection = database.GetCollection<TextKeyValue>("TextKeyValues");
        }

        public async Task CreateAsync(TextKeyValue textKeyValue)
        {
            await _textCollection.InsertOneAsync(textKeyValue);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var result = await _textCollection.DeleteOneAsync(x => x.Id == id);
            return result.IsAcknowledged && result.DeletedCount > 0;
        }

        public async Task<long> DeleteByYearAsync(int year)
        {
            var result = await _textCollection.DeleteManyAsync(x => x.Year == year);
            return result.IsAcknowledged ? result.DeletedCount : 0;
        }

        public async Task<IEnumerable<TextKeyValue>> GetAllAsync(int year)
        {
            return await _textCollection.Find(x => x.Year == year).ToListAsync();
        }

        public async Task<TextKeyValue> GetByIdAsync(Guid id)
        {
            return await _textCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<TextKeyValue> GetByKeyAsync(string key, int year)
        {
            return await _textCollection.Find(x => x.Key == key && x.Year == year).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<TextKeyValue>> GetDeployedBeforeAsync(DateTime date, int year)
        {
            return await _textCollection.Find(x => x.Year == year && x.DeployedDate < date).ToListAsync();
        }

        public async Task<bool> UpdateAsync(TextKeyValue textKeyValue)
        {
            var result = await _textCollection.ReplaceOneAsync(x => x.Id == textKeyValue.Id, textKeyValue);
            return result.IsAcknowledged && result.ModifiedCount > 0;
        }

        public async Task<IReadOnlyList<int>> GetDistinctYearsAsync()
        {
            var cursor = await _textCollection.DistinctAsync(x => x.Year, FilterDefinition<TextKeyValue>.Empty);
            var years = await cursor.ToListAsync();
            return years;
        }
    }
}

