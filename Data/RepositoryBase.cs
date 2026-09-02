using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ArnoxPay.Data;

public class RepositoryBase<T>(AppDbContext context) : IRepositoryBase<T> where T : class
{
    protected readonly AppDbContext Context = context;
    public IQueryable<T> FindAll() => Context.Set<T>().AsNoTracking();
    public IQueryable<T> FindByCondition(Expression<Func<T, bool>> expression) => Context.Set<T>().Where(expression).AsNoTracking();
    public T? GetById(object id) => Context.Set<T>().Find(id);
    public void Create(T entity) => Context.Set<T>().Add(entity);
    public void Update(T entity) => Context.Set<T>().Update(entity);
    public void Delete(T entity) => Context.Set<T>().Remove(entity);
}
